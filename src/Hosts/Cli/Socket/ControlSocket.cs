using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Basil.Host.Cli.Commands;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Host.Cli.Socket;

/// <summary>The local control socket: its path and the framing of a request and its reply.</summary>
public static class ControlSocket
{
	/// <summary>The file name of the socket inside the data directory.</summary>
	public const string FileName = "basil.sock";

	/// <summary>The longest path a Unix domain socket can have on Linux, minus a margin for the terminator.</summary>
	public const int MaxPathLength = 100;

	private const string OutputPrefix = "o ";
	private const string ExitPrefix = "x ";

	/// <summary>Gets the path of the socket for a data directory.</summary>
	public static string PathIn(string dataDirectory)
	{
		return Path.Combine(Path.GetFullPath(dataDirectory), FileName);
	}

	/// <summary>Frames a line of output for the client.</summary>
	public static IEnumerable<string> FrameOutput(string text)
	{
		return text.Split('\n').Select(line => OutputPrefix + line.TrimEnd('\r'));
	}

	/// <summary>Frames the end of a reply.</summary>
	public static string FrameExit(int code)
	{
		return ExitPrefix + code;
	}

	/// <summary>Reads a framed reply: writes the output lines and returns the exit code.</summary>
	/// <returns>The exit code, or <see langword="null" /> when the reply ended without one.</returns>
	public static async Task<int?> ReadReplyAsync(StreamReader reader, TextWriter output, CancellationToken cancellationToken)
	{
		while (await reader.ReadLineAsync(cancellationToken) is { } line)
		{
			if (line.StartsWith(OutputPrefix, StringComparison.Ordinal)) await output.WriteLineAsync(line[OutputPrefix.Length..]);
			else if (line.StartsWith(ExitPrefix, StringComparison.Ordinal) && int.TryParse(line[ExitPrefix.Length..], out var code))
				return code;
		}

		return null;
	}

	/// <summary>Connects to a socket.</summary>
	/// <returns>The connected socket, or <see langword="null" /> when nothing listens there.</returns>
	public static async Task<System.Net.Sockets.Socket?> TryConnectAsync(string path, CancellationToken cancellationToken)
	{
		if (path.Length > MaxPathLength || !File.Exists(path)) return null;

		var socket = new System.Net.Sockets.Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
		try
		{
			await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cancellationToken);
			return socket;
		}
		catch (SocketException)
		{
			socket.Dispose();
			return null;
		}
	}
}

/// <summary>Runs a command in a server that is running, over its local socket.</summary>
public static class ControlClient
{
	/// <summary>Opens a connection to the running server.</summary>
	/// <param name="path">The path of the socket.</param>
	/// <param name="cancellationToken">A token that cancels the connection.</param>
	/// <returns>The open connection, or <see langword="null" /> when no server is running.</returns>
	public static async Task<ControlConnection?> ConnectAsync(string path, CancellationToken cancellationToken)
	{
		return await ControlSocket.TryConnectAsync(path, cancellationToken) is { } socket ? new ControlConnection(socket) : null;
	}
}

/// <summary>An open connection to a running server, which takes one command.</summary>
public sealed class ControlConnection(System.Net.Sockets.Socket socket) : IDisposable
{
	/// <summary>Sends a command and writes the reply as it arrives.</summary>
	/// <param name="args">The words of the command line.</param>
	/// <param name="output">Where the output goes.</param>
	/// <param name="cancellationToken">A token that cancels the wait.</param>
	/// <returns>The exit code of the command.</returns>
	public async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter output, CancellationToken cancellationToken)
	{
		await using var stream = new NetworkStream(socket, false);
		var request = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(args) + "\n");
		await stream.WriteAsync(request, cancellationToken);
		using var reader = new StreamReader(stream, Encoding.UTF8);
		return await ControlSocket.ReadReplyAsync(reader, output, cancellationToken) ?? ExitCodes.NotRunning;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		socket.Dispose();
	}
}

/// <summary>Listens on the local control socket of a running server and runs the commands it receives as the operator.</summary>
/// <param name="runner">Runs the commands.</param>
/// <param name="path">The path of the socket.</param>
/// <param name="logger">Where problems are logged.</param>
public sealed class ControlServer(CommandRunner runner, string path, ILogger<ControlServer> logger) : BackgroundService
{
	private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);
	private System.Net.Sockets.Socket? _listener;

	/// <inheritdoc />
	public override async Task StartAsync(CancellationToken cancellationToken)
	{
		if (path.Length > ControlSocket.MaxPathLength)
		{
			logger.LogError("The control socket path is longer than {Max} characters, so the server runs without it: {Path}",
				ControlSocket.MaxPathLength, path);
			return;
		}

		if (await ControlSocket.TryConnectAsync(path, cancellationToken) is { } other)
		{
			other.Dispose();
			throw new InvalidOperationException($"Another Basil server is running: it answers on {path}.");
		}

		if (File.Exists(path)) File.Delete(path);

		_listener = new System.Net.Sockets.Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
		_listener.Bind(new UnixDomainSocketEndPoint(path));
		if (!OperatingSystem.IsWindows())
			File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
		_listener.Listen(8);
		await base.StartAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (_listener is null) return;

		while (!stoppingToken.IsCancellationRequested)
		{
			System.Net.Sockets.Socket client;
			try
			{
				client = await _listener.AcceptAsync(stoppingToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (SocketException exception)
			{
				logger.LogWarning(exception, "The control socket could not accept a connection");
				continue;
			}

			_ = Task.Run(() => ServeAsync(client, stoppingToken), stoppingToken);
		}
	}

	/// <inheritdoc />
	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		await base.StopAsync(cancellationToken);
		_listener?.Dispose();
		_listener = null;
		try
		{
			if (File.Exists(path)) File.Delete(path);
		}
		catch (IOException)
		{
		}
	}

	private async Task ServeAsync(System.Net.Sockets.Socket client, CancellationToken stoppingToken)
	{
		using var socket = client;
		try
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
			timeout.CancelAfter(RequestTimeout);
			await using var stream = new NetworkStream(socket, false);
			using var reader = new StreamReader(stream, Encoding.UTF8);
			var line = await reader.ReadLineAsync(timeout.Token);
			await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
			var args = line is null ? null : JsonSerializer.Deserialize<string[]>(line);
			if (args is null or { Length: 0 })
			{
				await writer.WriteLineAsync(ControlSocket.FrameExit(ExitCodes.Invalid));
				return;
			}

			var output = new SocketOutput(writer);
			var code = await runner.RunAsync(args, output, true, null, stoppingToken);
			await writer.WriteLineAsync(ControlSocket.FrameExit(code));
		}
		catch (Exception exception) when (exception is IOException or SocketException or JsonException or OperationCanceledException)
		{
			logger.LogDebug(exception, "A control connection ended");
		}
	}

	private sealed class SocketOutput(StreamWriter writer) : ICommandOutput
	{
		public void Write(OutputLine line)
		{
			foreach (var framed in ControlSocket.FrameOutput(line.Text)) writer.WriteLine(framed);
		}
	}
}

using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Basil.Host.Cli.Commands;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Host.Cli.Socket;

public static class ControlSocket
{
	public const string FileName = "basil.sock";

	public const int MaxPathLength = 100;

	private const string OutputPrefix = "o ";
	private const string ExitPrefix = "x ";

	// Beside the data when that path fits a socket name; a deep data directory gets a short path in the temporary
	// directory instead, named after the data directory, so the server and the shell still agree on it.
	public static string PathIn(string directory)
	{
		var full = Path.GetFullPath(directory);
		var beside = Path.Combine(full, FileName);
		if (beside.Length <= MaxPathLength) return beside;

		var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..12].ToLowerInvariant();
		// The user's own runtime directory first: a shared temporary directory lets another account answer in the server's place.
		var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
		return Path.Combine(string.IsNullOrEmpty(runtime) ? Path.GetTempPath() : runtime, $"basil-{hash}.sock");
	}

	public static IEnumerable<string> FrameOutput(string text)
	{
		return text.Split('\n').Select(line => OutputPrefix + line.TrimEnd('\r'));
	}

	public static string FrameExit(int code)
	{
		return ExitPrefix + code;
	}

	public static async Task<int?> ReadReplyAsync(StreamReader reader, TextWriter output,
		CancellationToken ct)
	{
		while (await reader.ReadLineAsync(ct) is { } line)
			if (line.StartsWith(OutputPrefix, StringComparison.Ordinal))
				await output.WriteLineAsync(line[OutputPrefix.Length..]);
			else if (line.StartsWith(ExitPrefix, StringComparison.Ordinal) &&
			         int.TryParse(line[ExitPrefix.Length..], out var code))
				return code;

		return null;
	}

	public static async Task<System.Net.Sockets.Socket?> TryConnectAsync(string path,
		CancellationToken ct)
	{
		if (path.Length > MaxPathLength || !File.Exists(path)) return null;

		var socket = new System.Net.Sockets.Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
		try
		{
			await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), ct);
			return socket;
		}
		catch (SocketException)
		{
			socket.Dispose();
			return null;
		}
	}
}

public static class ControlClient
{
	public static async Task<ControlConnection?> ConnectAsync(string path, CancellationToken ct)
	{
		return await ControlSocket.TryConnectAsync(path, ct) is { } socket
			? new ControlConnection(socket)
			: null;
	}
}

public sealed class ControlConnection(System.Net.Sockets.Socket socket) : IDisposable
{
	public void Dispose()
	{
		socket.Dispose();
	}

	public async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
	{
		await using var stream = new NetworkStream(socket, false);
		var request = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(args) + "\n");
		await stream.WriteAsync(request, ct);
		using var reader = new StreamReader(stream, Encoding.UTF8);
		return await ControlSocket.ReadReplyAsync(reader, output, ct) ?? ExitCodes.NotRunning;
	}
}

public sealed class ControlServer(CommandRunner runner, string path, ILogger<ControlServer> logger) : BackgroundService
{
	private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);
	private System.Net.Sockets.Socket? _listener;

	public override async Task StartAsync(CancellationToken ct)
	{
		if (path.Length > ControlSocket.MaxPathLength)
		{
			logger.LogError(
				"The control socket path is longer than {Max} characters, so the server runs without it: {Path}",
				ControlSocket.MaxPathLength, path);
			return;
		}

		if (await ControlSocket.TryConnectAsync(path, ct) is { } other)
		{
			other.Dispose();
			throw new InvalidOperationException($"Another Basil server is running: it answers on {path}.");
		}

		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		if (File.Exists(path)) File.Delete(path);

		_listener = new System.Net.Sockets.Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
		_listener.Bind(new UnixDomainSocketEndPoint(path));
		if (!OperatingSystem.IsWindows())
			File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
		_listener.Listen(8);
		await base.StartAsync(ct);
	}

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

	public override async Task StopAsync(CancellationToken ct)
	{
		await base.StopAsync(ct);
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
			await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
			writer.AutoFlush = true;
			writer.NewLine = "\n";
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
		catch (Exception exception) when (exception is IOException or SocketException or JsonException
			                                  or OperationCanceledException)
		{
			logger.LogDebug(exception, "A control connection ended");
		}
		catch (Exception exception)
		{
			logger.LogWarning(exception, "A control connection failed");
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
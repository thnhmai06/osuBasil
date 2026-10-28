using System.Net.Sockets;
using System.Text;

namespace Basil.Testing.Clients;

/// <summary>A minimal IRC client over a real socket; Phase 2 extends it with CAP, SASL and tag handling.</summary>
public sealed class IrcClient : IDisposable
{
	private readonly TcpClient _tcp = new();
	private StreamReader? _reader;
	private StreamWriter? _writer;

	public async Task ConnectAsync(string host, int port)
	{
		await _tcp.ConnectAsync(host, port);
		var stream = _tcp.GetStream();
		_reader = new StreamReader(stream, Encoding.UTF8);
		_writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
	}

	public Task SendAsync(string line) => _writer!.WriteLineAsync(line);

	public async Task<string?> ReadLineAsync(TimeSpan timeout)
	{
		using var cts = new CancellationTokenSource(timeout);
		return await _reader!.ReadLineAsync(cts.Token);
	}

	public void Dispose() => _tcp.Dispose();
}

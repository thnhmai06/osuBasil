using System.Net;
using System.Net.Sockets;
using Basil.Protocol.Irc;

namespace Basil.Host.Irc.Tests;

/// <summary>A client that does not read cannot make the server queue lines without end.</summary>
public sealed class SlowClientTests
{
	private static async Task<(IrcSession Session, TcpClient Client)> ConnectAsync()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var client = new TcpClient();
		await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port,
			TestContext.Current.CancellationToken);
		var server = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
		return (new IrcSession(server, IPAddress.Loopback, new IrcOptions(), TimeProvider.System), client);
	}

	private static async Task<bool> IsClosedAsync(TcpClient client)
	{
		var buffer = new byte[1];
		return await client.GetStream().ReadAsync(buffer, TestContext.Current.CancellationToken) == 0;
	}

	[Fact]
	public async Task AClientWithTooManyLinesWaiting_IsDisconnected()
	{
		var (session, client) = await ConnectAsync();
		using var _ = client;

		for (var i = 0; i <= IrcSession.MaxQueuedLines; i++) session.Send(new IrcMessage("PING", null, "x"));

		Assert.True(await IsClosedAsync(client));
	}

	[Fact]
	public async Task AClientWithTooManyCharactersWaiting_IsDisconnected()
	{
		var (session, client) = await ConnectAsync();
		using var _ = client;
		var text = new string('x', 400);

		for (var sent = 0; sent <= IrcSession.MaxQueuedCharacters; sent += text.Length)
			session.Send(new IrcMessage("NOTICE", null, "x", text));

		Assert.True(await IsClosedAsync(client));
	}
}

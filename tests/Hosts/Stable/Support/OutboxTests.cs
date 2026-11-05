using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Registry;
using Basil.Host.Stable.Support;
using Basil.Testing.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Stable.Tests.Support;

/// <summary>A client that never polls cannot make the server queue packets without end.</summary>
[Collection("Stable host")]
public sealed class OutboxTests(StableHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	[Fact]
	public async Task AClientWithTooManyBytesWaiting_IsDisconnected()
	{
		using var client = await Host.NewClientAsync("OutboxBytes");
		await client.LoginAsync();
		var connection = Host.Services.GetRequiredService<UserRegistry>().Find(client.Token!)!;
		var outbox = Host.Services.GetRequiredService<Outbox>();
		var text = new string('x', 64 * 1024);

		for (var sent = 0L; sent <= Outbox.MaxQueuedBytes; sent += text.Length)
			outbox.Send(connection, writer => writer.Notification(text));

		Assert.False(connection.IsOpen);
	}

	[Fact]
	public async Task AClientWithTooManyPacketsWaiting_IsDisconnected()
	{
		using var client = await Host.NewClientAsync("OutboxCount");
		await client.LoginAsync();
		var connection = Host.Services.GetRequiredService<UserRegistry>().Find(client.Token!)!;
		var outbox = Host.Services.GetRequiredService<Outbox>();

		for (var sent = 0; sent <= Outbox.MaxQueuedPackets; sent++)
			outbox.Send(connection, writer => writer.Pong());

		Assert.False(connection.IsOpen);
	}

	[Fact]
	public async Task TheBotsGameClientConnection_NeverQueuesAndIsNeverDisconnected()
	{
		var registry = Host.Services.GetRequiredService<UserRegistry>();
		await registry.EnsureBotAsync(TestContext.Current.CancellationToken);
		var bot = await registry.ConnectBotAsync(TestContext.Current.CancellationToken);
		var connection = bot!.Connections.OfType<StableConnection>().Single();
		var outbox = Host.Services.GetRequiredService<Outbox>();

		for (var sent = 0; sent <= Outbox.MaxQueuedPackets; sent++)
			outbox.Send(connection, writer => writer.Pong());

		Assert.True(connection.IsOpen);
		Assert.False(outbox.HasPending(connection));
	}

	[Fact]
	public async Task AClientThatPolls_StaysConnected()
	{
		using var client = await Host.NewClientAsync("OutboxPolls");
		await client.LoginAsync();
		var connection = Host.Services.GetRequiredService<UserRegistry>().Find(client.Token!)!;
		var outbox = Host.Services.GetRequiredService<Outbox>();

		for (var round = 0; round < 3; round++)
		{
			for (var i = 0; i < Outbox.MaxQueuedPackets / 2; i++) outbox.Send(connection, writer => writer.Pong());
			outbox.Drain(connection);
		}

		Assert.True(connection.IsOpen);
	}
}
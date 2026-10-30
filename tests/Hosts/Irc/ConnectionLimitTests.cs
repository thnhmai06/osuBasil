using System.Net;
using Basil.Application.Content.Persistence;
using Basil.Host.Irc.Support;
using Basil.Testing.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Irc.Tests;

/// <summary>The limit on how many IRC connections one address may hold open at the same time.</summary>
[Collection("IRC host")]
public sealed class ConnectionLimitTests(IrcHostFixture fixture)
{
	private static readonly IPAddress SecondLoopback = IPAddress.Parse("127.0.0.2");

	private BasilHost Host => fixture.Host;

	[Fact]
	public async Task Connections_UpToTheLimit_AreAccepted()
	{
		await WithConnectionLimitAsync(2, async () =>
		{
			using var first = await IrcClient.ConnectAsync(Host);
			using var second = await IrcClient.ConnectAsync(Host);

			await AssertAcceptedAsync(first, "first");
			await AssertAcceptedAsync(second, "second");
		});
	}

	[Fact]
	public async Task Connection_BeyondTheLimit_IsClosedWithTooManyConnections()
	{
		await WithConnectionLimitAsync(2, async () =>
		{
			using var first = await IrcClient.ConnectAsync(Host);
			using var second = await IrcClient.ConnectAsync(Host);
			await AssertAcceptedAsync(first, "first");
			await AssertAcceptedAsync(second, "second");

			using var refused = await IrcClient.ConnectAsync(Host);

			Assert.Equal($"ERROR :Closing Link: basil.test ({IrcReplies.TooManyConnections})",
				await refused.ExpectAsync("ERROR"));
			Assert.Null(await refused.ReadLineAsync());
		});
	}

	[Fact]
	public async Task Connection_FromAnotherAddress_IsNotCountedAgainstTheLimit()
	{
		await WithConnectionLimitAsync(1, async () =>
		{
			using var first = await IrcClient.ConnectAsync(Host);
			using var elsewhere = await IrcClient.ConnectAsync(Host, SecondLoopback);

			await AssertAcceptedAsync(first, "first address");
			await AssertAcceptedAsync(elsewhere, "second address");
		});
	}

	/// <summary>Sets the connection limit one address may use, and puts the previous settings back afterwards.</summary>
	private async Task WithConnectionLimitAsync(int perIp, Func<Task> body)
	{
		var settings = Host.Services.GetRequiredService<ISettingsRepository>();
		var original = await settings.GetAsync();
		await settings.CreateOrUpdateAsync(original with
		{
			RateLimits = original.RateLimits with { Connections = new ServerSettings.ConnectionLimitSettings { PerIp = perIp } }
		});
		try
		{
			await body();
		}
		finally
		{
			await settings.CreateOrUpdateAsync(original);
		}
	}

	/// <summary>Asserts that the server keeps a connection, by answering a <c>PING</c> with a <c>PONG</c>.</summary>
	private static async Task AssertAcceptedAsync(IrcClient client, string which)
	{
		await client.SendAsync("PING accepted");
		var line = await client.ReadLineAsync();

		Assert.True(line is not null && IrcClient.CommandOf(line) == "PONG",
			$"The {which} connection was not accepted; the server answered: {line ?? "nothing, it closed the socket"}.");
	}
}

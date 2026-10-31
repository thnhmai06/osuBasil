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
	// Each test calls from addresses of its own, so connections that earlier tests are still closing do not count.
	private static readonly IPAddress WithinLimit = IPAddress.Parse("127.20.0.1");
	private static readonly IPAddress BeyondLimit = IPAddress.Parse("127.20.0.2");
	private static readonly IPAddress FirstAddress = IPAddress.Parse("127.20.0.3");
	private static readonly IPAddress SecondAddress = IPAddress.Parse("127.20.0.4");

	private BasilHost Host => fixture.Host;

	[Fact]
	public async Task Connections_UpToTheLimit_AreAccepted()
	{
		await WithConnectionLimitAsync(2, async () =>
		{
			using var first = await IrcClient.ConnectAsync(Host, WithinLimit);
			using var second = await IrcClient.ConnectAsync(Host, WithinLimit);

			await AssertAcceptedAsync(first, "first");
			await AssertAcceptedAsync(second, "second");
		});
	}

	[Fact]
	public async Task Connection_BeyondTheLimit_IsClosedWithTooManyConnections()
	{
		await WithConnectionLimitAsync(2, async () =>
		{
			using var first = await IrcClient.ConnectAsync(Host, BeyondLimit);
			using var second = await IrcClient.ConnectAsync(Host, BeyondLimit);
			await AssertAcceptedAsync(first, "first");
			await AssertAcceptedAsync(second, "second");

			using var refused = await IrcClient.ConnectAsync(Host, BeyondLimit);

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
			using var first = await IrcClient.ConnectAsync(Host, FirstAddress);
			using var elsewhere = await IrcClient.ConnectAsync(Host, SecondAddress);

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
			RateLimits = original.RateLimits with
			{
				Connections = new ServerSettings.ConnectionLimitSettings { PerIp = perIp }
			}
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
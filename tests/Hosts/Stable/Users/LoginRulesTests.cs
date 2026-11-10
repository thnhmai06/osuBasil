using Basil.Application.Content.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Basil.Host.Stable.Support;
using Basil.Testing.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Stable.Tests.Users;

/// <summary>The rules around logging in: closed sessions, restrictions, failed-attempt limits and restarts.</summary>
[Collection("Stable host")]
public sealed class LoginRulesTests(StableHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	[Fact]
	public async Task Login_AfterTheSessionWasClosedByTheServer_SignsInAgainWithTheSavedPassword()
	{
		using var client = await Host.NewClientAsync("ClosedUser");
		await client.LoginAsync();
		var registry = Host.Services.GetRequiredService<UserRegistry>();
		var connection = registry.Find(client.Token!)!;
		Host.Time.Advance(TimeSpan.FromSeconds(2));
		await connection.User.ScopeTo(connection).Sessions.CloseAsync(connection.Session.Id, TestContext.Current.CancellationToken);

		await client.LoginAsync();

		Assert.True(client.LoginCode > 0);
	}

	[Fact]
	public async Task Login_SilencedAccount_StillLogsInAndIsToldHowLongTheSilenceLasts()
	{
		var record = await Host.CreateUserAsync("SilencedOne");
		record.Value.SilencedUntil = Host.Time.GetUtcNow().AddHours(1);
		await Host.Services.GetRequiredService<IUserRepository>().UpdateAsync(record, TestContext.Current.CancellationToken);
		using var client = OsuClient.Create(Host, "SilencedOne", "password");

		var packets = await client.LoginAsync();

		Assert.True(client.LoginCode > 0);
		Assert.Contains(packets, packet => packet.Id == OsuClient.In.SilenceEnd);
		Assert.DoesNotContain(packets, packet => packet.Id == OsuClient.In.AccountRestricted);
	}

	[Fact]
	public async Task Login_AfterTooManyWrongPasswords_IsRefusedEvenWithTheRightOne()
	{
		using var client = await Host.NewClientAsync("LockedOut");
		var limit = (await Host.Services.GetRequiredService<ISettingsRepository>().GetAsync(TestContext.Current.CancellationToken)).RateLimits.Login
			.MaxAttempts;
		for (var attempt = 0; attempt < limit; attempt++) await client.LoginAsync("wrong", "203.0.113.7");

		var packets = await client.LoginAsync(forwardedFor: "203.0.113.7");

		Assert.Equal(-1, client.LoginCode);
		Assert.Equal(StableReplies.TooManyAttempts,
			packets.First(packet => packet.Id == OsuClient.In.Notification).Strings(1)[0]);
	}

	[Fact]
	public async Task Login_FromAnotherAddress_IsNotAffectedByTheLockout()
	{
		using var client = await Host.NewClientAsync("OtherAddress");
		var limit = (await Host.Services.GetRequiredService<ISettingsRepository>().GetAsync(TestContext.Current.CancellationToken)).RateLimits.Login
			.MaxAttempts;
		for (var attempt = 0; attempt < limit; attempt++) await client.LoginAsync("wrong", "203.0.113.8");

		await client.LoginAsync(forwardedFor: "203.0.113.9");

		Assert.True(client.LoginCode > 0);
	}

	[Fact]
	public async Task Login_BeyondTheConnectionLimitOfOneAddress_IsRefused()
	{
		var settings = Host.Services.GetRequiredService<ISettingsRepository>();
		var original = await settings.GetAsync(TestContext.Current.CancellationToken);
		await settings.CreateOrUpdateAsync(original with
		{
			RateLimits = original.RateLimits with
			{
				Connections = new ServerSettings.ConnectionLimitSettings { PerIp = 1 }
			}
		}, TestContext.Current.CancellationToken);
		try
		{
			using var first = await Host.NewClientAsync("ConnOne");
			using var second = await Host.NewClientAsync("ConnTwo");
			await first.LoginAsync(forwardedFor: "203.0.113.20");

			await second.LoginAsync(forwardedFor: "203.0.113.20");

			Assert.True(first.LoginCode > 0);
			Assert.Equal(-5, second.LoginCode);
		}
		finally
		{
			await settings.CreateOrUpdateAsync(original with
			{
				RateLimits = original.RateLimits with
				{
					Connections = new ServerSettings.ConnectionLimitSettings { PerIp = 10_000 }
				}
			}, TestContext.Current.CancellationToken);
		}
	}

	[Fact]
	public async Task Login_SecondGameClientOfTheSameUser_IsRefusedWithTheTooManySessionsNotice()
	{
		using var first = await Host.NewClientAsync("TwoClients");
		using var second = OsuClient.Create(Host, "TwoClients", "password");
		await first.LoginAsync();

		var packets = await second.LoginAsync();

		Assert.Equal(-1, second.LoginCode);
		Assert.Equal(StableReplies.TooManySessions,
			packets.First(packet => packet.Id == OsuClient.In.Notification).Strings(1)[0]);
	}

	[Fact]
	public async Task Restart_AnnouncedByTheServer_ReachesEveryOnlineClientWithTheDelay()
	{
		using var client = await Host.NewClientAsync("RestartWatcher");
		await client.LoginAsync();

		Host.Services.GetRequiredService<RestartAnnouncer>().Announce(TimeSpan.FromSeconds(5));
		var restart = await client.WaitForAsync(OsuClient.In.Restart);

		Assert.Equal(5000, restart?.AsInt());
	}

	[Fact]
	public async Task Login_WhileTheServerStopsAcceptingLogins_IsRefusedWithoutAToken()
	{
		var state = Host.Services.GetRequiredService<StableState>();
		using var client = await Host.NewClientAsync("StoppingLogin");
		state.AcceptingLogins = false;
		try
		{
			await client.LoginAsync();
		}
		finally
		{
			state.AcceptingLogins = true;
		}

		Assert.Equal(-5, client.LoginCode);
	}
}
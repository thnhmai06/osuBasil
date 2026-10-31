using Basil.Application.Content.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Basil.Host.Bancho.Support;
using Basil.Testing.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Bancho.Tests.Users;

/// <summary>The rules around logging in: closed sessions, restrictions, failed-attempt limits and restarts.</summary>
[Collection("Bancho host")]
public sealed class LoginRulesTests(BanchoHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	[Fact]
	public async Task Login_AfterTheSessionWasClosedByTheServer_IsAskedForThePasswordOnce()
	{
		using var client = await Host.NewClientAsync("ReauthUser");
		await client.LoginAsync();
		var registry = Host.Services.GetRequiredService<UserRegistry>();
		var connection = registry.Find(client.Token!)!;
		Host.Time.Advance(TimeSpan.FromSeconds(2));
		await connection.User.ScopeTo(connection).Sessions.CloseAsync(connection.Session.Id);

		var firstReply = await client.LoginAsync();
		var first = client.LoginCode;
		await client.LoginAsync();

		// The notification names why a refused login was refused (a failure has been seen only on a busy CI host).
		var notice = string.Join(" | ", firstReply.Where(packet => packet.Id == 24)
			.Select(packet =>
			{
				var position = 0;
				return packet.ReadString(ref position);
			}));
		Assert.True(first == -7, $"First login answered {first} (notice: '{notice}'), expected -7.");
		Assert.True(client.LoginCode > 0);
	}

	[Fact]
	public async Task Login_RestrictedAccount_StillLogsInAndIsToldItIsRestricted()
	{
		var record = await Host.CreateUserAsync("RestrictedOne");
		await Host.Services.GetRequiredService<IRestrictionRepository>().CreateAsync(new RestrictionData
		{
			User = record, Permissions = RestrictionData.Restricted, StartsAt = Host.Time.GetUtcNow().AddDays(-1),
			EndsAt = null
		});
		using var client = OsuClient.Create(Host, "RestrictedOne", "password");

		var packets = await client.LoginAsync();

		Assert.True(client.LoginCode > 0);
		Assert.Contains(packets, packet => packet.Id == OsuClient.In.AccountRestricted);
	}

	[Fact]
	public async Task Login_AfterTooManyWrongPasswords_IsRefusedEvenWithTheRightOne()
	{
		using var client = await Host.NewClientAsync("LockedOut");
		var limit = (await Host.Services.GetRequiredService<ISettingsRepository>().GetAsync()).RateLimits.Login
			.MaxAttempts;
		for (var attempt = 0; attempt < limit; attempt++) await client.LoginAsync("wrong", "203.0.113.7");

		var packets = await client.LoginAsync(forwardedFor: "203.0.113.7");

		Assert.Equal(-1, client.LoginCode);
		Assert.Equal(BanchoReplies.TooManyAttempts,
			packets.First(packet => packet.Id == OsuClient.In.Notification).Strings(1)[0]);
	}

	[Fact]
	public async Task Login_FromAnotherAddress_IsNotAffectedByTheLockout()
	{
		using var client = await Host.NewClientAsync("OtherAddress");
		var limit = (await Host.Services.GetRequiredService<ISettingsRepository>().GetAsync()).RateLimits.Login
			.MaxAttempts;
		for (var attempt = 0; attempt < limit; attempt++) await client.LoginAsync("wrong", "203.0.113.8");

		await client.LoginAsync(forwardedFor: "203.0.113.9");

		Assert.True(client.LoginCode > 0);
	}

	[Fact]
	public async Task Login_BeyondTheConnectionLimitOfOneAddress_IsRefused()
	{
		var settings = Host.Services.GetRequiredService<ISettingsRepository>();
		var original = await settings.GetAsync();
		await settings.CreateOrUpdateAsync(original with
		{
			RateLimits = original.RateLimits with
			{
				Connections = new ServerSettings.ConnectionLimitSettings { PerIp = 1 }
			}
		});
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
			});
		}
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
		var state = Host.Services.GetRequiredService<BanchoState>();
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
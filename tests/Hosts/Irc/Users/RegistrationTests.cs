using Basil.Application.Content.Persistence;
using Basil.Testing.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Irc.Tests.Users;

/// <summary>Signing in to IRC: the key, the nickname rule, SASL, capabilities and the welcome.</summary>
[Collection("IRC host")]
public sealed class RegistrationTests(IrcHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	[Fact]
	public async Task Pass_ValidKey_WelcomesWithNumerics001To005()
	{
		var (irc, key) = await Host.NewIrcClientAsync("RegOk");
		using var _ = irc;

		var welcome = await irc.RegisterAsync("RegOk", key);

		Assert.Equal(":irc.basil.test 001 RegOk :Welcome to the Basil IRC network, RegOk!RegOk@basil.test", welcome);
		await irc.ExpectAsync("005");
		await irc.ExpectAsync("422");
		Assert.Contains(irc.Received, line => IrcClient.CommandOf(line) == "002");
		Assert.Contains(irc.Received, line => IrcClient.CommandOf(line) == "003");
		Assert.Contains(irc.Received, line => IrcClient.CommandOf(line) == "004");
	}

	[Fact]
	public async Task Welcome_Isupport_AdvertisesTheServersRules()
	{
		var (irc, key) = await Host.NewIrcClientAsync("RegSupport");
		using var _ = irc;
		await irc.RegisterAsync("RegSupport", key);

		var support = await irc.ExpectAsync("005");

		Assert.Equal(
			":irc.basil.test 005 RegSupport CASEMAPPING=ascii CHANTYPES=# PREFIX=(ov)@+ CHANMODES=,,, NETWORK=Basil NICKLEN=15 MODES=1 MAXTARGETS=1 LINELEN=512 :are supported by this server",
			support);
	}

	[Fact]
	public async Task Pass_NickDiffersOnlyInCase_IsAccepted()
	{
		var (irc, key) = await Host.NewIrcClientAsync("RegCase");
		using var _ = irc;

		var welcome = await irc.RegisterAsync("regcase", key);

		Assert.StartsWith(":irc.basil.test 001 RegCase ", welcome);
	}

	[Fact]
	public async Task Pass_NameWithSpace_IsTheNameWithAnUnderscore()
	{
		var (irc, key) = await Host.NewIrcClientAsync("Reg Space");
		using var _ = irc;

		var welcome = await irc.RegisterAsync("Reg_Space", key);

		Assert.StartsWith(":irc.basil.test 001 Reg_Space ", welcome);
	}

	[Fact]
	public async Task Pass_NickOfAnotherUser_IsRefusedWith464AndTheConnectionCloses()
	{
		var (irc, key) = await Host.NewIrcClientAsync("RegOwner");
		using var _ = irc;
		await Host.CreateUserAsync("RegOther");

		await irc.SendAsync($"PASS {key}");
		await irc.SendAsync("NICK RegOther");
		await irc.SendAsync("USER x 0 * :x");

		Assert.Equal(":irc.basil.test 464 * :Password incorrect", await irc.ExpectAsync("464"));
		Assert.Equal("ERROR :Closing Link: basil.test (Password incorrect)", await irc.ExpectAsync("ERROR"));
		Assert.Null(await irc.ReadLineAsync());
	}

	[Fact]
	public async Task Pass_WrongKey_IsRefusedWith464()
	{
		var (irc, _) = await Host.NewIrcClientAsync("RegWrongKey");
		using var __ = irc;

		await irc.SendAsync("PASS not-a-key");
		await irc.SendAsync("NICK RegWrongKey");
		await irc.SendAsync("USER x 0 * :x");

		await irc.ExpectAsync("464");
	}

	[Fact]
	public async Task Pass_RevokedKey_IsRefusedWith464()
	{
		var (irc, key) = await Host.NewIrcClientAsync("RegRevoked");
		using var _ = irc;
		await Host.RevokeIrcKeyAsync("RegRevoked", key);

		await irc.SendAsync($"PASS {key}");
		await irc.SendAsync("NICK RegRevoked");
		await irc.SendAsync("USER x 0 * :x");

		await irc.ExpectAsync("464");
	}

	[Fact]
	public async Task Registration_WithoutAnyCredential_IsRefusedWith464()
	{
		var (irc, _) = await Host.NewIrcClientAsync("RegNoPass");
		using var __ = irc;

		await irc.SendAsync("NICK RegNoPass");
		await irc.SendAsync("USER x 0 * :x");

		await irc.ExpectAsync("464");
	}

	[Fact]
	public async Task AccountPassword_IsNotAKey()
	{
		var (irc, _) = await Host.NewIrcClientAsync("RegPassword");
		using var __ = irc;

		await irc.SendAsync("PASS password");
		await irc.SendAsync("NICK RegPassword");
		await irc.SendAsync("USER x 0 * :x");

		await irc.ExpectAsync("464");
	}

	[Fact]
	public async Task Command_BeforeRegistration_Gets451()
	{
		var (irc, _) = await Host.NewIrcClientAsync("RegEarly");
		using var __ = irc;

		await irc.SendAsync("JOIN #osu");

		Assert.Equal(":irc.basil.test 451 * :You have not registered", await irc.ExpectAsync("451"));
	}

	[Fact]
	public async Task Ping_BeforeRegistration_IsAnswered()
	{
		var (irc, _) = await Host.NewIrcClientAsync("RegPing");
		using var __ = irc;

		await irc.SendAsync("PING hello");

		Assert.Equal(":irc.basil.test PONG irc.basil.test :hello", await irc.ExpectAsync("PONG"));
	}

	[Theory]
	[InlineData("NICK", "NICK RegAgain", "462")]
	[InlineData("USER", "USER a 0 * :a", "462")]
	[InlineData("PASS", "PASS x", "462")]
	public async Task IdentityCommand_AfterRegistration_Gets462(string name, string line, string numeric)
	{
		var (irc, key) = await Host.NewIrcClientAsync("RegAgain" + name);
		using var _ = irc;
		await irc.RegisterAsync("RegAgain" + name, key);

		await irc.SendAsync(line);

		await irc.ExpectAsync(numeric);
	}

	[Fact]
	public async Task SecondSocketWithTheSameKey_IsClosed()
	{
		var (first, key) = await Host.NewIrcClientAsync("RegTwice");
		using var _ = first;
		await first.RegisterAsync("RegTwice", key);
		using var second = await IrcClient.ConnectAsync(Host);

		await second.SendAsync($"PASS {key}");
		await second.SendAsync("NICK RegTwice");
		await second.SendAsync("USER x 0 * :x");

		Assert.Equal("ERROR :Closing Link: basil.test (This key is already connected.)",
			await second.ExpectAsync("ERROR"));
		await first.BarrierAsync();
	}

	[Fact]
	public async Task TwoKeysOfOneUser_AreTwoConnections()
	{
		var (first, key) = await Host.NewIrcClientAsync("RegDevices");
		using var _ = first;
		var secondKey = await Host.CreateIrcKeyAsync("RegDevices", "other-device");
		using var second = await IrcClient.ConnectAsync(Host);

		await first.RegisterAsync("RegDevices", key);
		var welcome = await second.RegisterAsync("RegDevices", secondKey);

		Assert.StartsWith(":irc.basil.test 001", welcome);
	}

	[Fact]
	public async Task Quit_ClosesWithErrorLine()
	{
		var (irc, key) = await Host.NewIrcClientAsync("RegQuit");
		using var _ = irc;
		await irc.RegisterAsync("RegQuit", key);

		await irc.SendAsync("QUIT :bye");

		Assert.Equal("ERROR :Closing Link: basil.test (Quit: bye)", await irc.ExpectAsync("ERROR"));
		Assert.Null(await irc.ReadLineAsync());
	}

	[Fact]
	public async Task RepeatedWrongKeysForOneAccountFromOneAddress_AreRefusedAsTooManyAttempts()
	{
		var settings = Host.Services.GetRequiredService<ISettingsRepository>();
		var before = await settings.GetAsync();
		await settings.CreateOrUpdateAsync(before with
		{
			RateLimits = before.RateLimits with { Login = new ServerSettings.LoginLimitSettings { MaxAttempts = 1 } }
		});
		// Failed logins of other tests count against the address of this one until their window ends.
		Host.Time.Advance(before.RateLimits.Login.Window + TimeSpan.FromSeconds(1));
		try
		{
			var (first, key) = await Host.NewIrcClientAsync("RegLimited");
			using var _ = first;
			await first.SendAsync("PASS wrong");
			await first.SendAsync("NICK RegLimited");
			await first.SendAsync("USER x 0 * :x");
			Assert.Equal(":irc.basil.test 464 * :Password incorrect", await first.ExpectAsync("464"));

			using var second = await IrcClient.ConnectAsync(Host);
			await second.SendAsync($"PASS {key}");
			await second.SendAsync("NICK RegLimited");
			await second.SendAsync("USER x 0 * :x");

			Assert.Equal(":irc.basil.test 464 * :Too many failed login attempts. Try again later.",
				await second.ExpectAsync("464"));
		}
		finally
		{
			await settings.CreateOrUpdateAsync(before);
		}
	}
}
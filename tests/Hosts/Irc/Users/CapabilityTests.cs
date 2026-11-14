using Basil.Testing.Clients;

namespace Basil.Host.Irc.Tests.Users;

/// <summary>Capability negotiation and SASL PLAIN, with the wire text of the IRCv3 specifications.</summary>
[Collection("IRC host")]
public sealed class CapabilityTests(IrcHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	[Fact]
	public async Task CapLs302_ListsSaslMechanism()
	{
		var (irc, _) = await Host.NewIrcClientAsync("CapLs");
		using var _ = irc;

		await irc.SendAsync("CAP LS 302");

		Assert.Equal(
			":irc.basil.test CAP * LS :cap-notify sasl=PLAIN server-time message-tags account-tag account-notify away-notify extended-join multi-prefix userhost-in-names echo-message standard-replies",
			await irc.ExpectAsync("CAP"));
	}

	[Fact]
	public async Task VersionlessCapLs_SaslNoValue()
	{
		var (irc, _) = await Host.NewIrcClientAsync("CapLsOld");
		using var _ = irc;

		await irc.SendAsync("CAP LS");

		var line = await irc.ExpectAsync("CAP");

		Assert.Contains(" sasl ", line);
		Assert.DoesNotContain("sasl=", line);
	}

	[Theory]
	[InlineData("invite-notify")]
	[InlineData("batch")]
	[InlineData("labeled-response")]
	[InlineData("chathistory")]
	[InlineData("sts")]
	public async Task UnsupportedCapReq_NakedAsWhole(string unsupported)
	{
		var (irc, _) = await Host.NewIrcClientAsync("Nak" + unsupported[..3] + unsupported.Length);
		using var _ = irc;

		await irc.SendAsync($"CAP REQ :echo-message {unsupported}");

		Assert.Equal($":irc.basil.test CAP * NAK :echo-message {unsupported}", await irc.ExpectAsync("CAP"));
	}

	[Fact]
	public async Task SupportedCapReq_AcknowledgedAndListed()
	{
		var (irc, _) = await Host.NewIrcClientAsync("CapAck");
		using var _ = irc;

		await irc.SendAsync("CAP REQ :server-time echo-message");
		Assert.Equal(":irc.basil.test CAP * ACK :server-time echo-message", await irc.ExpectAsync("CAP"));
		await irc.SendAsync("CAP LIST");

		Assert.Equal(":irc.basil.test CAP * LIST :server-time echo-message", await irc.ExpectAsync("CAP"));
	}

	[Fact]
	public async Task PendingCapEnd_RegistrationWaits()
	{
		var (irc, key) = await Host.NewIrcClientAsync("CapWait");
		using var _ = irc;

		await irc.SendAsync("CAP LS 302");
		await irc.SendAsync($"PASS {key}");
		await irc.SendAsync("NICK CapWait");
		await irc.SendAsync("USER x 0 * :x");
		var before = await irc.BarrierAsync();
		await irc.SendAsync("CAP END");
		var welcome = await irc.ExpectAsync("001");

		Assert.DoesNotContain(before, line => IrcClient.CommandOf(line) == "001");
		Assert.StartsWith(":irc.basil.test 001 CapWait ", welcome);
	}

	[Fact]
	public async Task CorrectSaslKey_SucceedsAndWelcomes()
	{
		var (irc, key) = await Host.NewIrcClientAsync("SaslOk");
		using var _ = irc;

		var outcome = await irc.RegisterWithSaslAsync("SaslOk", "SaslOk", key);

		Assert.Equal(":irc.basil.test 903 SaslOk :SASL authentication successful", outcome);
		Assert.Contains("900 SaslOk SaslOk!SaslOk@basil.test SaslOk :You are now logged in as SaslOk",
			string.Join('\n', irc.Received));
		Assert.StartsWith(":irc.basil.test 001 SaslOk ", await irc.ExpectAsync("001"));
	}

	[Fact]
	public async Task WrongSaslKey_Fails904Unregistered()
	{
		var (irc, _) = await Host.NewIrcClientAsync("SaslBad");
		using var __ = irc;

		var outcome = await irc.RegisterWithSaslAsync("SaslBad", "SaslBad", "nope");

		Assert.Equal(":irc.basil.test 904 * :SASL authentication failed", outcome);
	}

	[Fact]
	public async Task ForeignAuthzid_Fails904()
	{
		var (irc, key) = await Host.NewIrcClientAsync("SaslMine");
		using var _ = irc;
		await Host.CreateUserAsync("SaslTheirs");

		var outcome = await irc.RegisterWithSaslAsync("SaslMine", "SaslTheirs", key);

		Assert.Equal(":irc.basil.test 904 * :SASL authentication failed", outcome);
	}

	[Fact]
	public async Task ForeignNick_RenamedToAccount()
	{
		var (irc, key) = await Host.NewIrcClientAsync("SaslRename");
		using var _ = irc;

		await irc.RegisterWithSaslAsync("SomethingElse", "SaslRename", key);

		Assert.Equal(":SomethingElse NICK SaslRename", await irc.ReadUntilAsync(line => line.Contains(" NICK ")));
		Assert.StartsWith(":irc.basil.test 001 SaslRename ", await irc.ExpectAsync("001"));
	}

	[Fact]
	public async Task StarAuthenticate_Aborts906()
	{
		var (irc, _) = await Host.NewIrcClientAsync("SaslAbort");
		using var __ = irc;
		await irc.SendAsync("CAP REQ :sasl");
		await irc.ExpectAsync("CAP");
		await irc.SendAsync("AUTHENTICATE PLAIN");
		await irc.ExpectAsync("AUTHENTICATE");

		await irc.SendAsync("AUTHENTICATE *");

		Assert.Equal(":irc.basil.test 906 * :SASL authentication aborted", await irc.ExpectAsync("906"));
	}

	[Fact]
	public async Task NoSaslCapability_Fails904()
	{
		var (irc, _) = await Host.NewIrcClientAsync("SaslNoCap");
		using var __ = irc;

		await irc.SendAsync("AUTHENTICATE PLAIN");

		await irc.ExpectAsync("904");
	}

	[Fact]
	public async Task UnknownMechanism_ListsPlain()
	{
		var (irc, _) = await Host.NewIrcClientAsync("SaslMech");
		using var __ = irc;
		await irc.SendAsync("CAP REQ :sasl");
		await irc.ExpectAsync("CAP");

		await irc.SendAsync("AUTHENTICATE EXTERNAL");

		Assert.Equal(":irc.basil.test 908 * PLAIN :are available SASL mechanisms", await irc.ExpectAsync("908"));
		await irc.ExpectAsync("904");
	}

	[Fact]
	public async Task AuthenticateTwice_Gets907()
	{
		var (irc, key) = await Host.NewIrcClientAsync("SaslTwice");
		using var _ = irc;
		await irc.RegisterWithSaslAsync("SaslTwice", "SaslTwice", key);
		await irc.ExpectAsync("001");

		await irc.SendAsync("AUTHENTICATE PLAIN");

		Assert.Equal(":irc.basil.test 907 SaslTwice :You have already authenticated", await irc.ExpectAsync("907"));
	}
}
using Basil.Application.Persistence.Chat;
using Basil.Application.Runtime.Chat;
using Basil.Application.Runtime.Sessions;
using Basil.Domain.Chat;
using Basil.Domain.Permissions;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Basil.Application.Runtime.Tests.Chat;

/// <summary>
///     Behaviour of <see cref="ScopedChannel" /> and <see cref="ScopedGeneralChannel" /> that
///     <see cref="ScopedChannelTests" /> does not reach: reconnecting members, closed channels, the exact message-length
///     boundary, private-message exceptions, and what a configuration change applies, rejects and persists. Expected
///     values come from the XML contracts of the two types.
/// </summary>
public sealed class ScopedChannelRulesTests
{
	private const string ChannelName = "#rules";

	private static async Task<GeneralChannel> OpenChannelAsync(TestApplication app, Privileges read = Privileges.None,
		Privileges write = Privileges.None)
	{
		var admin = await app.CreateUserAsync("RulesAdmin", "password", PermissionsGroups.All);
		var by = await app.LoginAsync(admin);
		var record = new GeneralChannelRecord
		{
			Name = ChannelName,
			Value = new GeneralChannelData { ReadPrivileges = read, WritePrivileges = write }
		};
		Assert.True((await app.Server.ScopeTo(by).Channels.CreateAsync(record, TestContext.Current.CancellationToken))
			.IsOk);
		return app.Channels.Find(ChannelName)!;
	}

	private static async Task<Connection> AdminAsync(TestApplication app, string name = "Configurer")
	{
		return await app.LoginAsync(await app.CreateUserAsync(name, "password", PermissionsGroups.All));
	}

	#region Join

	[Fact]
	public async Task Join_AfterThePreviousConnectionOfTheSameKindClosed_ReplacesItAndTheEventReportsIt()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var user = await app.CreateUserAsync("Reconnects", "password");
		var first = await app.LoginAsync(user, ConnectionType.Irc);
		Assert.True(channel.ScopeTo(first).Join().IsOk);
		app.Users.Close(first, ConnectionCloseReason.LoggedOut);
		var second = await app.LoginAsync(user, ConnectionType.Irc);

		var result = channel.ScopeTo(second).Join();

		Assert.Equal(ChannelOutcome.Ok, result.Outcome);
		Assert.Equal([second], channel.Members);
		var joined = Assert.IsType<ChannelMemberJoined>(app.ChannelEvents.Events.Last());
		Assert.Equal(first, joined.Replaced);
		Assert.Equal(second, joined.Member);
	}

	[Fact]
	public async Task Join_FirstTimeOfAUser_ReportsNoReplacedConnection()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var connection = await app.LoginAsync(await app.CreateUserAsync("Fresh", "password"), ConnectionType.Irc);

		channel.ScopeTo(connection).Join();

		Assert.Null(Assert.IsType<ChannelMemberJoined>(app.ChannelEvents.Events.Last()).Replaced);
	}

	[Fact]
	public async Task Join_TwoApiConnectionsOfOneUser_AreBothMembers()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var user = await app.CreateUserAsync("TwoTabs", "password");
		var first = await app.LoginAsync(user);
		var second = await app.LoginAsync(user);

		var firstResult = channel.ScopeTo(first).Join();
		var secondResult = channel.ScopeTo(second).Join();

		Assert.True(firstResult.IsOk);
		Assert.True(secondResult.IsOk);
		Assert.Equal(2, channel.Members.Count);
		Assert.Null(Assert.IsType<ChannelMemberJoined>(app.ChannelEvents.Events.Last()).Replaced);
	}

	[Fact]
	public async Task Join_ConnectionOfAnotherKindOfTheSameUser_IsNotAReplacement()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var user = await app.CreateUserAsync("IrcAndGame", "password");
		var irc = await app.LoginAsync(user, ConnectionType.Irc);
		var game = await app.LoginAsync(user, ConnectionType.Bancho);
		channel.ScopeTo(irc).Join();

		var result = channel.ScopeTo(game).Join();

		Assert.Equal(ChannelOutcome.Ok, result.Outcome);
		Assert.Equal(2, channel.Members.Count);
	}

	[Fact]
	public async Task Join_ClosedChannelByAConnectionWithoutReadRights_ReturnsClosedBeforeAccessDenied()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app, Privileges.Moderator);
		var admin = await AdminAsync(app);
		await channel.ScopeTo(admin).General!.DeleteAsync(TestContext.Current.CancellationToken);
		var player = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));

		var result = channel.ScopeTo(player).Join();

		Assert.Equal(ChannelOutcome.Closed, result.Outcome);
	}

	[Fact]
	public async Task Part_ConnectionThatNeverJoinedAndTheOneThatAlreadyParted_BothGetNotMember()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var user = await app.CreateUserAsync("PartsTwice", "password");
		var connection = await app.LoginAsync(user);
		channel.ScopeTo(connection).Join();
		channel.ScopeTo(connection).Part();
		var eventsAfterFirstPart = app.ChannelEvents.Events.Count;

		var result = channel.ScopeTo(connection).Part();

		Assert.Equal(ChannelOutcome.NotMember, result.Outcome);
		Assert.Equal(eventsAfterFirstPart, app.ChannelEvents.Events.Count);
	}

	#endregion

	#region Post

	[Fact]
	public async Task PostAsync_ClosedChannel_ReturnsClosedAndPublishesNothing()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var member = await app.LoginAsync(await app.CreateUserAsync("Late", "password"));
		channel.ScopeTo(member).Join();
		var admin = await AdminAsync(app);
		await channel.ScopeTo(admin).General!.DeleteAsync(TestContext.Current.CancellationToken);
		var before = app.ChannelEvents.Events.Count;

		var result = await channel.ScopeTo(member).PostAsync("anyone?", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Closed, result.Outcome);
		Assert.Equal(before, app.ChannelEvents.Events.Count);
	}

	[Fact]
	public async Task PostAsync_ToPrivateChannelOfUserWhoWentOffline_ReturnsClosed()
	{
		using var app = new TestApplication();
		var ownerRecord = await app.CreateUserAsync("WentOffline", "password");
		var ownerConnection = await app.LoginAsync(ownerRecord, ConnectionType.Irc);
		var pm = app.Users.Of(ownerRecord).PmChannel;
		var sender = await app.LoginAsync(await app.CreateUserAsync("Sender", "password"));
		app.Users.Close(ownerConnection, ConnectionCloseReason.LoggedOut);

		var result = await pm.ScopeTo(sender).PostAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Closed, result.Outcome);
	}

	[Fact]
	public async Task PostAsync_TextOfExactlyTheLimit_IsKeptWhole()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var member = await app.LoginAsync(await app.CreateUserAsync("Exact", "password"));
		channel.ScopeTo(member).Join();
		var text = new string('b', Channel.MaxMessageLength);

		await channel.ScopeTo(member).PostAsync(text, cancellationToken: TestContext.Current.CancellationToken);

		var posted = Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events.Last());
		Assert.False(posted.Truncated);
		Assert.Equal(text, posted.Message.Content);
	}

	[Fact]
	public async Task PostAsync_NoticeFlag_IsCarriedOnTheMessage()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var member = await app.LoginAsync(await app.CreateUserAsync("Noticer", "password"));
		channel.ScopeTo(member).Join();

		await channel.ScopeTo(member).PostAsync("heads up", notice: true, cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events.Last()).Message.IsNotice);
	}

	[Fact]
	public async Task PostAsync_ChannelNeedsModeratorRoleToWrite_ModeratorIsAccepted()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app, write: Privileges.Moderator);
		var moderator = await app.LoginAsync(await app.CreateUserAsync("Mod", "password",
			PermissionsGroups.Default | Permissions.UserSilence));
		channel.ScopeTo(moderator).Join();

		var result = await channel.ScopeTo(moderator).PostAsync("official", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Ok, result.Outcome);
	}

	[Fact]
	public async Task PostAsync_AfterTheChannelRaisedItsWriteRole_PlayerAlreadyInsideIsRefused()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var player = await app.LoginAsync(await app.CreateUserAsync("Inside", "password"));
		channel.ScopeTo(player).Join();
		var admin = await AdminAsync(app);
		await channel.ScopeTo(admin).General!.ConfigureAsync(new GeneralChannelChange(WritePrivileges: Privileges.Moderator),
			TestContext.Current.CancellationToken);

		var result = await channel.ScopeTo(player).PostAsync("still here", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.AccessDenied, result.Outcome);
		Assert.Contains(player, channel.Members);
	}

	[Fact]
	public async Task PostAsync_ToPrivateChannelOfUserWhoBlockedSomeoneElse_StillGetsThrough()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("Selective", "password");
		var ownerConnection = await app.LoginAsync(owner, ConnectionType.Irc);
		var blockedRecord = await app.CreateUserAsync("TheBlocked", "password");
		await app.Users.Of(blockedRecord).ScopeTo(ownerConnection).Relationships
			.BlockAsync(TestContext.Current.CancellationToken);
		var sender = await app.LoginAsync(await app.CreateUserAsync("Innocent", "password"));

		var result = await app.Users.Of(owner).PmChannel.ScopeTo(sender)
			.PostAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Ok, result.Outcome);
	}

	[Fact]
	public async Task PostAsync_FriendsOnlyRecipientAndSenderHoldingChatMessageAnyone_GetsThrough()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("FriendsOwner", "password");
		var ownerConnection = await app.LoginAsync(owner, ConnectionType.Irc);
		Assert.True(app.Users.Of(owner).ScopeTo(ownerConnection).SetPmPrivate(true));
		var staff = await app.LoginAsync(await app.CreateUserAsync("StaffSender", "password",
			PermissionsGroups.Default | Permissions.ChatMessageAnyone));

		var result = await app.Users.Of(owner).PmChannel.ScopeTo(staff)
			.PostAsync("official", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Ok, result.Outcome);
	}

	[Fact]
	public async Task PostAsync_RecipientCannotPostPrivateMessagesAndSenderHoldsChatMessageAnyone_IsStillTargetSilenced()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("SilencedOwner", "password",
			PermissionsGroups.Default & ~Permissions.ChatPostPrivate);
		await app.LoginAsync(owner, ConnectionType.Irc);
		var staff = await app.LoginAsync(await app.CreateUserAsync("StaffToSilenced", "password",
			PermissionsGroups.Default | Permissions.ChatMessageAnyone));

		var result = await app.Users.Of(owner).PmChannel.ScopeTo(staff)
			.PostAsync("hello?", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.TargetSilenced, result.Outcome);
	}

	[Fact]
	public async Task PostAsync_ToYourOwnPrivateChannelWhileAway_GetsNoAwayReply()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("TalksToSelf", "password");
		var connection = await app.LoginAsync(owner, ConnectionType.Irc);
		app.Users.Of(owner).ScopeTo(connection).SetAway("brb");

		var result = await app.Users.Of(owner).PmChannel.ScopeTo(connection)
			.PostAsync("note to self", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Ok, result.Outcome);
		Assert.Null(Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events.Last()).AwayReply);
	}

	#endregion

	#region Scope shape

	[Fact]
	public async Task General_OnAPrivateMessageChannel_IsNull()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("PmOwner", "password");
		var connection = await app.LoginAsync(owner, ConnectionType.Irc);

		Assert.Null(app.Users.Of(owner).PmChannel.ScopeTo(connection).General);
	}

	[Fact]
	public async Task General_OnAGeneralChannel_ExposesThatChannel()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var connection = await AdminAsync(app, "Viewer");

		Assert.Same(channel, channel.ScopeTo(connection).General!.Channel);
	}

	#endregion

	#region Configure

	[Fact]
	public async Task ConfigureAsync_EveryFieldGiven_AppliesAllOfThem()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var admin = await AdminAsync(app);

		var result = await channel.ScopeTo(admin).General!.ConfigureAsync(
			new GeneralChannelChange("Welcome", Privileges.Player, Privileges.Moderator, true, false),
			TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Ok, result.Outcome);
		var settings = channel.Record.Value;
		Assert.Equal("Welcome", channel.Topic);
		Assert.Equal(Privileges.Player, settings.ReadPrivileges);
		Assert.Equal(Privileges.Moderator, settings.WritePrivileges);
		Assert.True(settings.AutoJoin);
		Assert.False(settings.Visible);
	}

	[Fact]
	public async Task ConfigureAsync_OnlyTopicGiven_LeavesTheOtherSettingsAsTheyWere()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app, Privileges.Player, Privileges.Moderator);
		var admin = await AdminAsync(app);
		var before = channel.Record.Value;
		var (autoJoin, visible) = (before.AutoJoin, before.Visible);

		await channel.ScopeTo(admin).General!.ConfigureAsync(new GeneralChannelChange(Topic: "Only topic"),
			TestContext.Current.CancellationToken);

		var after = channel.Record.Value;
		Assert.Equal("Only topic", channel.Topic);
		Assert.Equal(Privileges.Player, after.ReadPrivileges);
		Assert.Equal(Privileges.Moderator, after.WritePrivileges);
		Assert.Equal(autoJoin, after.AutoJoin);
		Assert.Equal(visible, after.Visible);
	}

	[Fact]
	public async Task ConfigureAsync_AutoJoinAndVisibleFlippedBackAndForth_FollowTheLastValue()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var scoped = channel.ScopeTo(await AdminAsync(app)).General!;

		await scoped.ConfigureAsync(new GeneralChannelChange(AutoJoin: true, Visible: false),
			TestContext.Current.CancellationToken);
		await scoped.ConfigureAsync(new GeneralChannelChange(AutoJoin: false, Visible: true),
			TestContext.Current.CancellationToken);

		Assert.False(channel.Record.Value.AutoJoin);
		Assert.True(channel.Record.Value.Visible);
	}

	[Fact]
	public async Task ConfigureAsync_Allowed_PublishesOneEventWithTheChange()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var admin = await AdminAsync(app);
		var change = new GeneralChannelChange(Topic: "News");
		var before = app.ChannelEvents.Events.Count;

		await channel.ScopeTo(admin).General!.ConfigureAsync(change, TestContext.Current.CancellationToken);

		var configured = Assert.IsType<ChannelConfigured>(Assert.Single(app.ChannelEvents.Events.Skip(before)));
		Assert.Equal(change, configured.Change);
		Assert.Empty(configured.Removed);
	}

	[Fact]
	public async Task ConfigureAsync_LoweringReadPrivileges_RemovesNobody()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app, Privileges.Moderator);
		var moderator = await app.LoginAsync(await app.CreateUserAsync("StaysIn", "password",
			PermissionsGroups.Default | Permissions.UserSilence));
		channel.ScopeTo(moderator).Join();
		var admin = await AdminAsync(app);

		await channel.ScopeTo(admin).General!.ConfigureAsync(new GeneralChannelChange(ReadPrivileges: Privileges.None),
			TestContext.Current.CancellationToken);

		Assert.Contains(moderator, channel.Members);
		Assert.Empty(Assert.IsType<ChannelConfigured>(app.ChannelEvents.Events.Last()).Removed);
	}

	[Fact]
	public async Task ConfigureAsync_Allowed_PersistsTheNewSettings()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var admin = await AdminAsync(app);
		var repository = app.Provider.GetRequiredService<IChannelRepository>();
		repository.ClearReceivedCalls();

		await channel.ScopeTo(admin).General!.ConfigureAsync(new GeneralChannelChange(Topic: "Saved"),
			TestContext.Current.CancellationToken);

		await repository.Received(1).CreateOrUpdateAsync(
			Arg.Is<GeneralChannelRecord>(record => record.Name == ChannelName && record.Value.Topic == "Saved"),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task ConfigureAsync_Denied_PersistsNothingAndChangesNothing()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var player = await app.LoginAsync(await app.CreateUserAsync("Meddler", "password"));
		var repository = app.Provider.GetRequiredService<IChannelRepository>();
		repository.ClearReceivedCalls();

		await channel.ScopeTo(player).General!.ConfigureAsync(new GeneralChannelChange(Topic: "Hijacked"),
			TestContext.Current.CancellationToken);

		await repository.DidNotReceiveWithAnyArgs().CreateOrUpdateAsync(default!, default);
		Assert.Empty(app.ChannelEvents.Events.OfType<ChannelConfigured>());
	}

	[Fact]
	public async Task ConfigureAsync_ServerConfigureSuspendedByRestriction_IsDenied()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var user = await app.CreateUserAsync("Suspended", "password", PermissionsGroups.All);
		app.AddRestriction(user, Permissions.ServerConfigure);
		var connection = await app.LoginAsync(user);

		var result = await channel.ScopeTo(connection).General!.ConfigureAsync(new GeneralChannelChange(Topic: "x"),
			TestContext.Current.CancellationToken);

		Assert.Equal(Permissions.ServerConfigure, result.Missing);
	}

	[Fact]
	public async Task ConfigureAsync_ClosedChannel_ReturnsClosed()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var admin = await AdminAsync(app);
		await channel.ScopeTo(admin).General!.DeleteAsync(TestContext.Current.CancellationToken);

		var result = await channel.ScopeTo(admin).General!.ConfigureAsync(new GeneralChannelChange(Topic: "late"),
			TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Closed, result.Outcome);
	}

	[Fact]
	public async Task ConfigureAsync_ClosedChannelByAConnectionWithoutServerConfigure_IsDeniedBeforeClosed()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var admin = await AdminAsync(app);
		await channel.ScopeTo(admin).General!.DeleteAsync(TestContext.Current.CancellationToken);
		var player = await app.LoginAsync(await app.CreateUserAsync("Nosy", "password"));

		var result = await channel.ScopeTo(player).General!.ConfigureAsync(new GeneralChannelChange(Topic: "x"),
			TestContext.Current.CancellationToken);

		Assert.Equal(Permissions.ServerConfigure, result.Missing);
	}

	[Fact]
	public async Task ConfigureAsync_UndefinedReadPrivilegeBit_ThrowsAndChangesNothing()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var admin = await AdminAsync(app);

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => channel.ScopeTo(admin).General!.ConfigureAsync(
			new GeneralChannelChange("Half applied", (Privileges)0x80), TestContext.Current.CancellationToken));

		Assert.NotEqual("Half applied", channel.Topic);
		Assert.Equal(Privileges.None, channel.Record.Value.ReadPrivileges);
	}

	[Fact]
	public async Task ConfigureAsync_UndefinedWritePrivilegeBit_ThrowsAndChangesNothing()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var admin = await AdminAsync(app);

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => channel.ScopeTo(admin).General!.ConfigureAsync(
			new GeneralChannelChange("Half applied", WritePrivileges: (Privileges)0x80),
			TestContext.Current.CancellationToken));

		Assert.NotEqual("Half applied", channel.Topic);
	}

	[Fact]
	public async Task ConfigureAsync_TopicContainingNul_ThrowsAndChangesNothing()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var admin = await AdminAsync(app);
		var topicBefore = channel.Topic;

		await Assert.ThrowsAsync<ArgumentException>(() => channel.ScopeTo(admin).General!.ConfigureAsync(
			new GeneralChannelChange(Topic: "bad\0topic"), TestContext.Current.CancellationToken));

		Assert.Equal(topicBefore, channel.Topic);
	}

	[Fact]
	public async Task DeleteAsync_Allowed_RemovesTheChannelFromStorage()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var admin = await AdminAsync(app);
		var repository = app.Provider.GetRequiredService<IChannelRepository>();

		await channel.ScopeTo(admin).General!.DeleteAsync(TestContext.Current.CancellationToken);

		await repository.Received(1).DeleteAsync(
			Arg.Is<GeneralChannelRecord>(record => record.Name == ChannelName), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task DeleteAsync_ServerConfigureSuspendedByRestriction_IsDeniedAndKeepsTheChannel()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var user = await app.CreateUserAsync("Barred", "password", PermissionsGroups.All);
		app.AddRestriction(user, Permissions.ServerConfigure);
		var connection = await app.LoginAsync(user);

		var result = await channel.ScopeTo(connection).General!.DeleteAsync(TestContext.Current.CancellationToken);

		Assert.Equal(Permissions.ServerConfigure, result.Missing);
		Assert.False(channel.IsClosed);
	}

	#endregion
}

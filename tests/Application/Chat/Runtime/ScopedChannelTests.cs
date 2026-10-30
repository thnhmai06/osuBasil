using Basil.Application.Chat.Runtime;
using Basil.Application.Users.Connections;
using Basil.Domain.Auth;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Chat.Tests.Runtime;

/// <summary>
///     Behaviour of a chat channel as a connection may use it. Expected values come from AGENTS.md and
///     docs/for-agents/identity-permissions.md (permissions, privileges, blocks yielding to a higher right).
/// </summary>
public sealed class ScopedChannelTests
{
	private const string ChannelName = "#lobby";

	private static async Task<GeneralChannel> OpenChannelAsync(TestApplication app,
		UserPrivileges read = UserPrivileges.None,
		UserPrivileges write = UserPrivileges.None)
	{
		var admin = await app.CreateUserAsync("ChannelAdmin", "password", UserPermissionsGroups.All);
		var by = await app.LoginAsync(admin);
		var record = new GeneralChannelRecord
		{
			Name = ChannelName,
			Value = new GeneralChannelData { ReadPrivileges = read, WritePrivileges = write }
		};
		var created = await app.Server.ScopeTo(by).Channels.CreateAsync(record, TestContext.Current.CancellationToken);
		Assert.True(created.IsOk);
		return app.Channels.Find(ChannelName)!;
	}

	private static async Task<Connection> MemberOfAsync(TestApplication app, GeneralChannel channel, string name,
		UserPermissions? permissions = null)
	{
		var user = await app.CreateUserAsync(name, "password", permissions);
		var connection = await app.LoginAsync(user);
		Assert.True(channel.ScopeTo(connection).Join().IsOk);
		return connection;
	}

	#region Join and part

	[Fact]
	public async Task Join_OpenChannel_AddsMemberAndPublishesEvent()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var user = await app.CreateUserAsync("Joiner", "password");
		var connection = await app.LoginAsync(user);

		var result = channel.ScopeTo(connection).Join();

		Assert.Equal(ChannelOutcome.Ok, result.Outcome);
		Assert.Contains(connection, channel.Members);
		var joined = Assert.IsType<ChannelMemberJoined>(app.ChannelEvents.Events[^1]);
		Assert.Equal(connection, joined.Member);
	}

	[Fact]
	public async Task Join_AlreadyMember_ReturnsAlreadyMember()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var connection = await MemberOfAsync(app, channel, "Twice");

		var result = channel.ScopeTo(connection).Join();

		Assert.Equal(ChannelOutcome.AlreadyMember, result.Outcome);
		Assert.Single(channel.Members, member => member == connection);
	}

	[Fact]
	public async Task Join_ChannelNeedsModeratorRole_PlayerIsRefused()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app, UserPrivileges.Moderator);
		var user = await app.CreateUserAsync("PlainPlayer", "password");
		var connection = await app.LoginAsync(user);

		var result = channel.ScopeTo(connection).Join();

		Assert.Equal(ChannelOutcome.AccessDenied, result.Outcome);
		Assert.DoesNotContain(connection, channel.Members);
	}

	[Fact]
	public async Task Join_ChannelNeedsModeratorRole_UserHoldingAModeratorPermissionIsAccepted()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app, UserPrivileges.Moderator);
		var user = await app.CreateUserAsync("Moderator", "password",
			UserPermissionsGroups.Default | UserPermissions.UserManageSilence);
		var connection = await app.LoginAsync(user);

		var result = channel.ScopeTo(connection).Join();

		Assert.Equal(ChannelOutcome.Ok, result.Outcome);
	}

	[Fact]
	public async Task Join_ModeratorWhoseRolePermissionsAreAllSuspended_IsRefused()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app, UserPrivileges.Moderator);
		var user = await app.CreateUserAsync("SuspendedMod", "password",
			UserPermissionsGroups.Default | UserPermissions.UserManageSilence);
		app.AddRestriction(user, UserPermissions.UserManageSilence);
		var connection = await app.LoginAsync(user);

		var result = channel.ScopeTo(connection).Join();

		Assert.Equal(ChannelOutcome.AccessDenied, result.Outcome);
	}

	[Fact]
	public async Task Join_ClosedChannel_ReturnsClosed()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var admin = await app.LoginAsync(await app.CreateUserAsync("Closer", "password", UserPermissionsGroups.All));
		await channel.ScopeTo(admin).General!.DeleteAsync(TestContext.Current.CancellationToken);
		var connection = await app.LoginAsync(await app.CreateUserAsync("Latecomer", "password"));

		var result = channel.ScopeTo(connection).Join();

		Assert.Equal(ChannelOutcome.Closed, result.Outcome);
	}

	[Fact]
	public async Task Part_Member_RemovesItAndPublishesEvent()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var connection = await MemberOfAsync(app, channel, "Leaver");

		var result = channel.ScopeTo(connection).Part();

		Assert.Equal(ChannelOutcome.Ok, result.Outcome);
		Assert.DoesNotContain(connection, channel.Members);
		var parted = Assert.IsType<ChannelMemberParted>(app.ChannelEvents.Events[^1]);
		Assert.Equal(connection, parted.Member);
		Assert.False(parted.Kicked);
	}

	[Fact]
	public async Task Part_NotMember_ReturnsNotMember()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var connection = await app.LoginAsync(await app.CreateUserAsync("Outsider", "password"));

		var result = channel.ScopeTo(connection).Part();

		Assert.Equal(ChannelOutcome.NotMember, result.Outcome);
	}

	#endregion

	#region Posting to a general channel

	[Fact]
	public async Task PostAsync_Member_PublishesTheMessageUnderTheirAccount()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var connection = await MemberOfAsync(app, channel, "Talker");

		var result = await channel.ScopeTo(connection)
			.PostAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Ok, result.Outcome);
		var posted = Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events[^1]);
		Assert.Equal("hello", posted.Message.Content);
		Assert.Equal(connection.Record, posted.Message.Author);
		Assert.Equal(connection, posted.By);
		Assert.False(posted.Truncated);
	}

	[Fact]
	public async Task PostAsync_LongerThanTheLimit_IsCutAndFlagged()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var connection = await MemberOfAsync(app, channel, "Rambler");

		await channel.ScopeTo(connection).PostAsync(new string('a', Channel.MaxMessageLength + 1),
			cancellationToken: TestContext.Current.CancellationToken);

		var posted = Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events[^1]);
		Assert.True(posted.Truncated);
		Assert.Equal(Channel.MaxMessageLength, posted.Message.Content.Length);
	}

	[Fact]
	public async Task PostAsync_EmptyText_ReturnsEmptyAndPublishesNothing()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var connection = await MemberOfAsync(app, channel, "Mute");
		var before = app.ChannelEvents.Events.Count;

		var result = await channel.ScopeTo(connection)
			.PostAsync("", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Empty, result.Outcome);
		Assert.Equal(before, app.ChannelEvents.Events.Count);
	}

	[Fact]
	public async Task PostAsync_WhitespaceOnlyText_IsPostedAsIs()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var connection = await MemberOfAsync(app, channel, "Mute");

		var result = await channel.ScopeTo(connection)
			.PostAsync("   ", cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(result.IsOk);
		var posted = Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events[^1]);
		Assert.Equal("   ", posted.Message.Content);
	}

	[Fact]
	public async Task PostAsync_NotAMember_ReturnsNotMember()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var connection = await app.LoginAsync(await app.CreateUserAsync("Lurker", "password"));

		var result = await channel.ScopeTo(connection)
			.PostAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.NotMember, result.Outcome);
	}

	[Fact]
	public async Task PostAsync_WithoutChatPost_IsDeniedNamingChatPost()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var connection =
			await MemberOfAsync(app, channel, "NoVoice", UserPermissionsGroups.Default & ~UserPermissions.ChatPost);

		var result = await channel.ScopeTo(connection)
			.PostAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

		Assert.False(result.IsOk);
		Assert.Equal(UserPermissions.ChatPost, result.Missing);
	}

	[Fact]
	public async Task PostAsync_ChatPostSuspendedByRestriction_IsDeniedNamingChatPost()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var user = await app.CreateUserAsync("PostSilenced", "password");
		app.AddRestriction(user, UserPermissions.ChatPost);
		var connection = await app.LoginAsync(user);
		Assert.True(channel.ScopeTo(connection).Join().IsOk);

		var result = await channel.ScopeTo(connection)
			.PostAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

		Assert.False(result.IsOk);
		Assert.Equal(UserPermissions.ChatPost, result.Missing);
	}

	[Fact]
	public async Task PostAsync_ChannelNeedsModeratorRoleToWrite_PlayerIsRefused()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app, write: UserPrivileges.Moderator);
		var connection = await MemberOfAsync(app, channel, "ReadOnlyPlayer");

		var result = await channel.ScopeTo(connection)
			.PostAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.AccessDenied, result.Outcome);
	}

	#endregion

	#region Private messages

	[Fact]
	public async Task PostAsync_ToOnlineUser_DeliversThePrivateMessage()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("PmOwner", "password");
		await app.LoginAsync(owner, SessionType.Irc);
		var sender = await app.LoginAsync(await app.CreateUserAsync("PmSender", "password"));
		var pm = app.PmChannels.Of(app.Users.Of(owner));

		var result = await pm.ScopeTo(sender)
			.PostAsync("psst", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Ok, result.Outcome);
		var posted = Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events[^1]);
		Assert.Same(pm, posted.Channel);
		Assert.Equal("psst", posted.Message.Content);
		Assert.Null(posted.AwayReply);
	}

	[Fact]
	public async Task PostAsync_ToOfflineUser_IsRefused()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("OfflineOwner", "password");
		var sender = await app.LoginAsync(await app.CreateUserAsync("OfflineSender", "password"));

		var result = await app.PmChannels.Of(app.Users.Of(owner)).ScopeTo(sender)
			.PostAsync("anyone?", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.AccessDenied, result.Outcome);
	}

	[Theory]
	[InlineData("a\0b")]
	[InlineData("\e[0m")]
	public async Task PostAsync_ControlCharacters_ReturnsInvalidTextAndPublishesNothing(string text)
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var connection = await MemberOfAsync(app, channel, "Ctrl");
		var before = app.ChannelEvents.Events.Count;

		var result = await channel.ScopeTo(connection)
			.PostAsync(text, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.InvalidText, result.Outcome);
		Assert.Equal(before, app.ChannelEvents.Events.Count);
	}

	[Fact]
	public async Task PostAsync_ToUserWithAwayMessage_CarriesTheAwayReplyInTheSameEvent()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("AwayOwner", "password");
		var ownerConnection = await app.LoginAsync(owner, SessionType.Irc);
		Assert.True(app.Users.Of(owner).ScopeTo(ownerConnection).Profile.SetAway("brb"));
		var sender = await app.LoginAsync(await app.CreateUserAsync("AwaySender", "password"));

		await app.PmChannels.Of(app.Users.Of(owner)).ScopeTo(sender)
			.PostAsync("hey", cancellationToken: TestContext.Current.CancellationToken);

		var posted = Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events[^1]);
		Assert.Equal("brb", posted.AwayReply?.Content);
		Assert.Equal(owner, posted.AwayReply?.Author);
	}

	[Fact]
	public async Task PostAsync_NoticeToUserWithAwayMessage_GetsNoAwayReply()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("NoticeOwner", "password");
		var ownerConnection = await app.LoginAsync(owner, SessionType.Irc);
		app.Users.Of(owner).ScopeTo(ownerConnection).Profile.SetAway("brb");
		var sender = await app.LoginAsync(await app.CreateUserAsync("NoticeSender", "password"));

		await app.PmChannels.Of(app.Users.Of(owner)).ScopeTo(sender)
			.PostAsync("fyi", true, TestContext.Current.CancellationToken);

		Assert.Null(Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events[^1]).AwayReply);
	}

	[Fact]
	public async Task PostAsync_SenderWithoutChatPostPrivate_IsDeniedNamingIt()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("DeniedOwner", "password");
		await app.LoginAsync(owner, SessionType.Irc);
		var sender = await app.LoginAsync(await app.CreateUserAsync("DeniedSender", "password",
			UserPermissionsGroups.Default & ~UserPermissions.ChatPostPrivate));

		var result = await app.PmChannels.Of(app.Users.Of(owner)).ScopeTo(sender)
			.PostAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

		Assert.False(result.IsOk);
		Assert.Equal(UserPermissions.ChatPostPrivate, result.Missing);
	}

	[Fact]
	public async Task PostAsync_RecipientBlockedTheSender_ReturnsBlocked()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("Blocker", "password");
		var ownerConnection = await app.LoginAsync(owner, SessionType.Irc);
		var senderRecord = await app.CreateUserAsync("Blocked", "password");
		var sender = await app.LoginAsync(senderRecord);
		await app.Users.Of(senderRecord).ScopeTo(ownerConnection).Relationships
			.BlockAsync(TestContext.Current.CancellationToken);

		var result = await app.PmChannels.Of(app.Users.Of(owner)).ScopeTo(sender)
			.PostAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Blocked, result.Outcome);
	}

	[Fact]
	public async Task PostAsync_BlockedSenderHoldingChatMessageAnyone_GetsThrough()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("BlockerOfStaff", "password");
		var ownerConnection = await app.LoginAsync(owner, SessionType.Irc);
		var staffRecord = await app.CreateUserAsync("Staff", "password",
			UserPermissionsGroups.Default | UserPermissions.ChatManageMessageAnyone);
		var staff = await app.LoginAsync(staffRecord);
		await app.Users.Of(staffRecord).ScopeTo(ownerConnection).Relationships
			.BlockAsync(TestContext.Current.CancellationToken);

		var result = await app.PmChannels.Of(app.Users.Of(owner)).ScopeTo(staff)
			.PostAsync("notice", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Ok, result.Outcome);
	}

	[Fact]
	public async Task PostAsync_FriendsOnlyRecipient_AcceptsFriendsAndRefusesStrangers()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("FriendsOnly", "password");
		var ownerConnection = await app.LoginAsync(owner, SessionType.Irc);
		Assert.True(app.Users.Of(owner).ScopeTo(ownerConnection).Profile.SetPmPrivate(true));
		var friendRecord = await app.CreateUserAsync("TheFriend", "password");
		var friend = await app.LoginAsync(friendRecord);
		var stranger = await app.LoginAsync(await app.CreateUserAsync("TheStranger", "password"));
		await app.Users.Of(friendRecord).ScopeTo(ownerConnection).Relationships
			.AddFriendAsync(TestContext.Current.CancellationToken);
		var pm = app.PmChannels.Of(app.Users.Of(owner));

		var fromFriend =
			await pm.ScopeTo(friend).PostAsync("hi", cancellationToken: TestContext.Current.CancellationToken);
		var fromStranger = await pm.ScopeTo(stranger)
			.PostAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Ok, fromFriend.Outcome);
		Assert.Equal(ChannelOutcome.Blocked, fromStranger.Outcome);
	}

	[Fact]
	public async Task PostAsync_RecipientCannotPostPrivateMessages_ReturnsTargetSilenced()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("SilencedOwner", "password");
		app.AddRestriction(owner, UserPermissions.ChatPostPrivate);
		await app.LoginAsync(owner, SessionType.Irc);
		var sender = await app.LoginAsync(await app.CreateUserAsync("SilencedSender", "password"));

		var result = await app.PmChannels.Of(app.Users.Of(owner)).ScopeTo(sender)
			.PostAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.TargetSilenced, result.Outcome);
	}

	[Fact]
	public async Task PostAsync_ToBasilBot_IsDeliveredEvenThoughTheBotHoldsNoPermissions()
	{
		using var app = new TestApplication();
		await app.SeedBotAsync();
		await app.Users.EnsureBotAsync(TestContext.Current.CancellationToken);
		await app.Users.ConnectBotAsync(TestContext.Current.CancellationToken);
		var sender = await app.LoginAsync(await app.CreateUserAsync("BotAsker", "password"));

		var result = await app.PmChannels.Of(app.Users.Of(app.Users.Online.Keys.Single(user => user.IsBasilBot())))
			.ScopeTo(sender).PostAsync("!help", cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Ok, result.Outcome);
	}

	[Fact]
	public async Task Join_OtherUsersPrivateMessageChannel_IsRefused()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("PmChannelOwner", "password");
		await app.LoginAsync(owner, SessionType.Irc);
		var intruder = await app.LoginAsync(await app.CreateUserAsync("Intruder", "password"));

		var result = app.PmChannels.Of(app.Users.Of(owner)).ScopeTo(intruder).Join();

		Assert.Equal(ChannelOutcome.AccessDenied, result.Outcome);
	}

	#endregion

	#region General channel management

	[Fact]
	public async Task ConfigureAsync_WithoutChatManageChannels_IsDeniedNamingIt()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var connection = await app.LoginAsync(await app.CreateUserAsync("NotAnAdmin", "password"));

		var result = await channel.ScopeTo(connection).General!.ConfigureAsync(new GeneralChannelChange("new"),
			TestContext.Current.CancellationToken);

		Assert.False(result.IsOk);
		Assert.Equal(UserPermissions.ChatManageChannels, result.Missing);
		Assert.NotEqual("new", channel.Topic);
	}

	[Fact]
	public async Task ConfigureAsync_RaisingReadPrivileges_RemovesMembersWhoCanNoLongerReadAndReportsThem()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var player = await MemberOfAsync(app, channel, "Demoted");
		var moderator = await MemberOfAsync(app, channel, "Kept",
			UserPermissionsGroups.Default | UserPermissions.UserManageSilence);
		var admin = await app.LoginAsync(await app.CreateUserAsync("Configurer", "password",
			UserPermissionsGroups.All));

		var result = await channel.ScopeTo(admin).General!.ConfigureAsync(
			new GeneralChannelChange(ReadPrivileges: UserPrivileges.Moderator), TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Ok, result.Outcome);
		Assert.DoesNotContain(player, channel.Members);
		Assert.Contains(moderator, channel.Members);
		var configured = Assert.IsType<ChannelConfigured>(app.ChannelEvents.Events[^1]);
		Assert.Equal([player], configured.Removed);
	}

	[Fact]
	public async Task DeleteAsync_WithoutChatManageChannels_IsDeniedAndLeavesTheChannelOpen()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var connection = await app.LoginAsync(await app.CreateUserAsync("NotAllowed", "password"));

		var result = await channel.ScopeTo(connection).General!.DeleteAsync(TestContext.Current.CancellationToken);

		Assert.Equal(UserPermissions.ChatManageChannels, result.Missing);
		Assert.False(channel.IsClosed);
		Assert.Same(channel, app.Channels.Find(ChannelName));
	}

	[Fact]
	public async Task DeleteAsync_Allowed_ClosesTheChannelRemovesMembersAndForgetsIt()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var member = await MemberOfAsync(app, channel, "Evicted");
		var admin = await app.LoginAsync(await app.CreateUserAsync("Deleter", "password", UserPermissionsGroups.All));

		var result = await channel.ScopeTo(admin).General!.DeleteAsync(TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Ok, result.Outcome);
		Assert.True(channel.IsClosed);
		Assert.Empty(channel.Members);
		Assert.Null(app.Channels.Find(ChannelName));
		var closed = Assert.IsType<ChannelClosed>(app.ChannelEvents.Events[^1]);
		Assert.Equal([member], closed.Members);
	}

	#endregion
}

using Basil.Application.Chat.Capabilities;
using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Objects.Events;
using Basil.Application.Chat.Persistence;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Persistence;
using Basil.Domain.Authentication;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Infrastructure.Chat.Handlers;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Basil.Application.Chat.Tests.Capabilities;

/// <summary>
///     Behaviour of <see cref="ScopedChannel" /> and <see cref="ScopedGeneralChannel" /> that
///     <see cref="ScopedChannelTests" /> does not reach: reconnecting members, closed channels, the exact message-length
///     boundary, private-message exceptions, and what a configuration change applies, rejects and persists. Expected
///     values come from the XML contracts of the two types.
/// </summary>
public sealed class ScopedChannelRulesTests
{
	private const string ChannelName = "#rules";

	private static async Task<GeneralChannel> OpenChannelAsync(TestApplication app,
		UserPrivileges read = UserPrivileges.None,
		UserPrivileges write = UserPrivileges.None)
	{
		var admin = await app.CreateUserAsync("RulesAdmin", "password", UserPrivilegesExtensions.All);
		var actor = await app.LoginAsync(admin);
		var record = new GeneralChannelRecord
		{
			Name = ChannelName,
			Value = new GeneralChannelData { ReadPrivileges = read, WritePrivileges = write }
		};
		Assert.True(
			(await app.Server.ScopeTo(actor).Channels.CreateAsync(record))
			 == ServerOutcome.Ok);
		return app.Channels.Find(ChannelName)!;
	}

	private static async Task<Connection> AdminAsync(TestApplication app, string name = "Configurer")
	{
		return await app.LoginAsync(await app.CreateUserAsync(name, "password", UserPrivilegesExtensions.All));
	}

	#region Join

	[Fact]
	public async Task SameKindClosed_ReplacesAndReports()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var user = await app.CreateUserAsync("Reconnects", "password");
		var first = await app.LoginAsync(user, SessionType.Stable);
		Assert.True(channel.ScopeTo(first).Join() == ChannelOutcome.Ok);
		app.Users.Close(first, SessionEndReason.ConnectionLost);
		var second = await app.LoginAsync(user, SessionType.Stable);

		var result = channel.ScopeTo(second).Join();

		Assert.Equal(ChannelOutcome.Ok, result);
		Assert.Equal([second], channel.Members);
		var joined = Assert.IsType<ChannelMemberJoined>(app.ChannelEvents.Events[^1]);
		Assert.Equal(first, joined.Replaced);
		Assert.Equal(second, joined.Member);
	}

	[Fact]
	public async Task FirstTime_ReportsNoReplaced()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var connection = await app.LoginAsync(await app.CreateUserAsync("Fresh", "password"), SessionType.Irc);

		channel.ScopeTo(connection).Join();

		Assert.Null(Assert.IsType<ChannelMemberJoined>(app.ChannelEvents.Events[^1]).Replaced);
	}

	[Fact]
	public async Task TwoApiConnections_BothMembers()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var user = await app.CreateUserAsync("TwoTabs", "password");
		var first = await app.LoginAsync(user);
		var second = await app.LoginAsync(user);

		var firstResult = channel.ScopeTo(first).Join();
		var secondResult = channel.ScopeTo(second).Join();

		Assert.True(firstResult == ChannelOutcome.Ok);
		Assert.True(secondResult == ChannelOutcome.Ok);
		Assert.Equal(2, channel.Members.Count);
		Assert.Null(Assert.IsType<ChannelMemberJoined>(app.ChannelEvents.Events[^1]).Replaced);
	}

	[Fact]
	public async Task OtherKind_NotReplacement()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var user = await app.CreateUserAsync("IrcAndGame", "password");
		var irc = await app.LoginAsync(user, SessionType.Irc);
		var game = await app.LoginAsync(user, SessionType.Stable);
		channel.ScopeTo(irc).Join();

		var result = channel.ScopeTo(game).Join();

		Assert.Equal(ChannelOutcome.Ok, result);
		Assert.Equal(2, channel.Members.Count);
	}

	[Fact]
	public async Task NoReadPrivileges_ClosedBeforeDenied()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app, UserPrivileges.Moderator);
		var admin = await AdminAsync(app);
		await channel.ScopeTo(admin).General!.Value.DeleteAsync();
		var player = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));

		var result = channel.ScopeTo(player).Join();

		Assert.Equal(ChannelOutcome.Closed, result);
	}

	[Fact]
	public async Task NeverJoinedParted_NotMember()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var user = await app.CreateUserAsync("PartsTwice", "password");
		var connection = await app.LoginAsync(user);
		channel.ScopeTo(connection).Join();
		channel.ScopeTo(connection).Part();
		var eventsAfterFirstPart = app.ChannelEvents.Events.Count;

		var result = channel.ScopeTo(connection).Part();

		Assert.Equal(ChannelOutcome.NotMember, result);
		Assert.Equal(eventsAfterFirstPart, app.ChannelEvents.Events.Count);
	}

	#endregion

	#region Post

	[Fact]
	public async Task ClosedChannel_ClosedNoPublish()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var member = await app.LoginAsync(await app.CreateUserAsync("Late", "password"));
		channel.ScopeTo(member).Join();
		var admin = await AdminAsync(app);
		await channel.ScopeTo(admin).General!.Value.DeleteAsync();
		var before = app.ChannelEvents.Events.Count;

		var result = await channel.ScopeTo(member)
			.PostAsync("anyone?", ct: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Closed, result);
		Assert.Equal(before, app.ChannelEvents.Events.Count);
	}

	[Fact]
	public async Task PrivateOfOffline_ReturnsClosed()
	{
		using var app = new TestApplication();
		var ownerRecord = await app.CreateUserAsync("WentOffline", "password");
		var ownerConnection = await app.LoginAsync(ownerRecord, SessionType.Irc);
		var pm = app.PmChannels.Of(app.Users.Of(ownerRecord));
		var sender = await app.LoginAsync(await app.CreateUserAsync("Sender", "password"));
		app.Users.Close(ownerConnection, SessionEndReason.LoggedOut);
		var closed = Assert.Single(app.UserEvents.Events.OfType<UserConnectionClosed>(),
			@event => @event.Connection == ownerConnection);

		await new PmConnectionClosedHandler(app.PmChannels).Handle(closed, TestContext.Current.CancellationToken);

		var result = await pm.ScopeTo(sender).PostAsync("hi", ct: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Closed, result);
	}

	[Fact]
	public async Task ClosedDuringCheck_ClosedNoPublish()
	{
		PmChannel? pm = null;
		var relationships = new ClosingRelationshipRepository(() => pm?.Close());
		using var app = new TestApplication(services =>
			services.AddSingleton<IRelationshipRepository>(relationships));
		var ownerRecord = await app.CreateUserAsync("ClosesInSend", "password");
		await app.LoginAsync(ownerRecord, SessionType.Irc);
		pm = app.PmChannels.Of(app.Users.Of(ownerRecord));
		var sender = await app.LoginAsync(await app.CreateUserAsync("CarefulSender", "password"));

		var result = await pm.ScopeTo(sender)
			.PostAsync("hi", ct: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Closed, result);
		Assert.DoesNotContain(app.ChannelEvents.Events, @event => @event is ChannelMessagePosted);
	}

	private sealed class ClosingRelationshipRepository(Action onList) : IRelationshipRepository
	{
		public Task CreateOrUpdateAsync(Relationship relationship)
		{
			return Task.CompletedTask;
		}

		public Task DeleteAsync(Relationship relationship)
		{
			return Task.CompletedTask;
		}

		public Task<IReadOnlyList<Relationship>> ListAsync(UserRecord actor,
			CancellationToken ct = default)
		{
			onList();
			return Task.FromResult<IReadOnlyList<Relationship>>([]);
		}
	}

	[Fact]
	public async Task TextAtLimit_KeptWhole()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var member = await app.LoginAsync(await app.CreateUserAsync("Exact", "password"));
		channel.ScopeTo(member).Join();
		var text = new string('b', Channel.MaxMessageLength);

		await channel.ScopeTo(member).PostAsync(text, ct: TestContext.Current.CancellationToken);

		var posted = Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events[^1]);
		Assert.False(posted.Truncated);
		Assert.Equal(text, posted.Message.Content);
	}

	[Fact]
	public async Task NoticeFlag_CarriedOnMessage()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var member = await app.LoginAsync(await app.CreateUserAsync("Noticer", "password"));
		channel.ScopeTo(member).Join();

		await channel.ScopeTo(member).PostAsync("heads up", true, TestContext.Current.CancellationToken);

		Assert.True(Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events[^1]).Message.IsNotice);
	}

	[Fact]
	public async Task ModeratorRole_ModeratorAccepted()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app, write: UserPrivileges.Moderator);
		var moderator = await app.LoginAsync(await app.CreateUserAsync("Mod", "password",
			UserPrivilegesExtensions.Default | UserPrivileges.Moderator));
		channel.ScopeTo(moderator).Join();

		var result = await channel.ScopeTo(moderator)
			.PostAsync("official", ct: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Ok, result);
	}

	[Fact]
	public async Task WriteRoleRaised_InsidePlayerRefused()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var player = await app.LoginAsync(await app.CreateUserAsync("Inside", "password"));
		channel.ScopeTo(player).Join();
		var admin = await AdminAsync(app);
		await channel.ScopeTo(admin).General!.Value.ConfigureAsync(
			new GeneralChannelChange(WritePrivileges: UserPrivileges.Moderator));

		var result = await channel.ScopeTo(player)
			.PostAsync("still here", ct: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.AccessDenied, result);
		Assert.Contains(player, channel.Members);
	}

	[Fact]
	public async Task PrivateOfBlocker_GetsThrough()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("Selective", "password");
		var ownerConnection = await app.LoginAsync(owner, SessionType.Irc);
		var blockedRecord = await app.CreateUserAsync("TheBlocked", "password");
		await app.Users.Of(blockedRecord).ScopeTo(ownerConnection).Relationships
			.BlockAsync();
		var sender = await app.LoginAsync(await app.CreateUserAsync("Innocent", "password"));

		var result = await app.PmChannels.Of(app.Users.Of(owner)).ScopeTo(sender)
			.PostAsync("hi", ct: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Ok, result);
	}

	[Fact]
	public async Task FriendsOnly_SenderPassesThrough()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("FriendsOwner", "password");
		var ownerConnection = await app.LoginAsync(owner, SessionType.Irc);
		Assert.True(await app.Users.Of(owner).ScopeTo(ownerConnection).Profile.SetPmPrivateAsync(true));
		var staff = await app.LoginAsync(await app.CreateUserAsync("StaffSender", "password",
			UserPrivilegesExtensions.Default | UserPrivileges.Moderator));

		var result = await app.PmChannels.Of(app.Users.Of(owner)).ScopeTo(staff)
			.PostAsync("official", ct: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Ok, result);
	}

	[Fact]
	public async Task
		PostAsync_RecipientIsSilencedAndSenderIsAModerator_IsStillTargetSilenced()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("SilencedOwner", "password");
		app.Silence(owner, app.Clock.GetUtcNow().AddHours(1));
		await app.LoginAsync(owner, SessionType.Irc);
		var staff = await app.LoginAsync(await app.CreateUserAsync("StaffToSilenced", "password",
			UserPrivilegesExtensions.Default | UserPrivileges.Moderator));

		var result = await app.PmChannels.Of(app.Users.Of(owner)).ScopeTo(staff)
			.PostAsync("hello?", ct: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.TargetSilenced, result);
	}

	[Fact]
	public async Task OwnPrivateAway_NoAwayReply()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("TalksToSelf", "password");
		var connection = await app.LoginAsync(owner, SessionType.Irc);
		await app.Users.Of(owner).ScopeTo(connection).Profile.SetAwayAsync("brb");

		var result = await app.PmChannels.Of(app.Users.Of(owner)).ScopeTo(connection)
			.PostAsync("note to self", ct: TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.Ok, result);
		Assert.Null(Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events[^1]).AwayReply);
	}

	#endregion

	#region Scope shape

	[Fact]
	public async Task PrivateMessageChannel_IsNull()
	{
		using var app = new TestApplication();
		var owner = await app.CreateUserAsync("PmOwner", "password");
		var connection = await app.LoginAsync(owner, SessionType.Irc);

		Assert.Null(app.PmChannels.Of(app.Users.Of(owner)).ScopeTo(connection).General);
	}

	[Fact]
	public async Task GeneralChannel_ExposesChannel()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var connection = await AdminAsync(app, "Viewer");

		Assert.Same(channel, channel.ScopeTo(connection).General!.Value.Channel);
	}

	#endregion

	#region Configure

	[Fact]
	public async Task EveryField_AppliesAll()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var admin = await AdminAsync(app);

		var result = await channel.ScopeTo(admin).General!.Value.ConfigureAsync(
			new GeneralChannelChange("Welcome", UserPrivileges.Player, UserPrivileges.Moderator, true, false));

		Assert.Equal(ChannelOutcome.Ok, result);
		var settings = channel.Record.Value;
		Assert.Equal("Welcome", channel.Topic);
		Assert.Equal(UserPrivileges.Player, settings.ReadPrivileges);
		Assert.Equal(UserPrivileges.Moderator, settings.WritePrivileges);
		Assert.True(settings.AutoJoin);
		Assert.False(settings.Visible);
	}

	[Fact]
	public async Task OnlyTopic_LeavesOthers()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app, UserPrivileges.Player, UserPrivileges.Moderator);
		var admin = await AdminAsync(app);
		var before = channel.Record.Value;
		var (autoJoin, visible) = (before.AutoJoin, before.Visible);

		await channel.ScopeTo(admin).General!.Value.ConfigureAsync(new GeneralChannelChange("Only topic"));

		var after = channel.Record.Value;
		Assert.Equal("Only topic", channel.Topic);
		Assert.Equal(UserPrivileges.Player, after.ReadPrivileges);
		Assert.Equal(UserPrivileges.Moderator, after.WritePrivileges);
		Assert.Equal(autoJoin, after.AutoJoin);
		Assert.Equal(visible, after.Visible);
	}

	[Fact]
	public async Task FlipBack_FollowsLastValue()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var scoped = channel.ScopeTo(await AdminAsync(app)).General!.Value;

		await scoped.ConfigureAsync(new GeneralChannelChange(AutoJoin: true, Visible: false));
		await scoped.ConfigureAsync(new GeneralChannelChange(AutoJoin: false, Visible: true));

		Assert.False(channel.Record.Value.AutoJoin);
		Assert.True(channel.Record.Value.Visible);
	}

	[Fact]
	public async Task Allowed_PublishesOneEvent()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var admin = await AdminAsync(app);
		var change = new GeneralChannelChange("News");
		var before = app.ChannelEvents.Events.Count;

		await channel.ScopeTo(admin).General!.Value.ConfigureAsync(change);

		var configured = Assert.IsType<ChannelConfigured>(Assert.Single(app.ChannelEvents.Events.Skip(before)));
		Assert.Equal(change, configured.Change);
		Assert.Empty(configured.Removed);
	}

	[Fact]
	public async Task LoweringReadPrivileges_RemovesNobody()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app, UserPrivileges.Moderator);
		var moderator = await app.LoginAsync(await app.CreateUserAsync("StaysIn", "password",
			UserPrivilegesExtensions.Default | UserPrivileges.Moderator));
		channel.ScopeTo(moderator).Join();
		var admin = await AdminAsync(app);

		await channel.ScopeTo(admin).General!.Value.ConfigureAsync(
			new GeneralChannelChange(ReadPrivileges: UserPrivileges.None));

		Assert.Contains(moderator, channel.Members);
		Assert.Empty(Assert.IsType<ChannelConfigured>(app.ChannelEvents.Events[^1]).Removed);
	}

	[Fact]
	public async Task Allowed_PersistsSettings()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var admin = await AdminAsync(app);
		var repository = app.Provider.GetRequiredService<IChannelRepository>();
		repository.ClearReceivedCalls();

		await channel.ScopeTo(admin).General!.Value.ConfigureAsync(new GeneralChannelChange("Saved"));

		await repository.Received(1).CreateOrUpdateAsync(
			Arg.Is<GeneralChannelRecord>(record => record.Name == ChannelName && record.Value.Topic == "Saved"));
	}

	[Fact]
	public async Task Denied_PersistsNothing()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var player = await app.LoginAsync(await app.CreateUserAsync("Meddler", "password"));
		var repository = app.Provider.GetRequiredService<IChannelRepository>();
		repository.ClearReceivedCalls();

		await channel.ScopeTo(player).General!.Value.ConfigureAsync(new GeneralChannelChange("Hijacked"));

		await repository.DidNotReceiveWithAnyArgs().CreateOrUpdateAsync(null!);
		Assert.Empty(app.ChannelEvents.Events.OfType<ChannelConfigured>());
	}

	[Fact]
	public async Task ClosedChannel_ReturnsClosed()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var admin = await AdminAsync(app);
		await channel.ScopeTo(admin).General!.Value.DeleteAsync();

		var result = await channel.ScopeTo(admin).General!.Value.ConfigureAsync(new GeneralChannelChange("late"));

		Assert.Equal(ChannelOutcome.Closed, result);
	}

	[Fact]
	public async Task ClosedNoManage_DeniedBeforeClosed()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var admin = await AdminAsync(app);
		await channel.ScopeTo(admin).General!.Value.DeleteAsync();
		var player = await app.LoginAsync(await app.CreateUserAsync("Nosy", "password"));

		var result = await channel.ScopeTo(player).General!.Value.ConfigureAsync(new GeneralChannelChange("x"));

		Assert.Equal(ChannelOutcome.Denied, result);
	}

	[Fact]
	public async Task UndefinedReadBit_ThrowsNoChange()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var admin = await AdminAsync(app);

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
			channel.ScopeTo(admin).General!.Value.ConfigureAsync(
				new GeneralChannelChange("Half applied", (UserPrivileges)0x80)));

		Assert.NotEqual("Half applied", channel.Topic);
		Assert.Equal(UserPrivileges.None, channel.Record.Value.ReadPrivileges);
	}

	[Fact]
	public async Task UndefinedWriteBit_ThrowsNoChange()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var admin = await AdminAsync(app);

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
			channel.ScopeTo(admin).General!.Value.ConfigureAsync(
				new GeneralChannelChange("Half applied", WritePrivileges: (UserPrivileges)0x80)));

		Assert.NotEqual("Half applied", channel.Topic);
	}

	[Fact]
	public async Task TopicWithNul_ThrowsNoChange()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var admin = await AdminAsync(app);
		var topicBefore = channel.Topic;

		await Assert.ThrowsAsync<ArgumentException>(() => channel.ScopeTo(admin).General!.Value.ConfigureAsync(
			new GeneralChannelChange("bad\0topic")));

		Assert.Equal(topicBefore, channel.Topic);
	}

	[Fact]
	public async Task Allowed_RemovesFromStorage()
	{
		using var app = new TestApplication();
		var channel = await OpenChannelAsync(app);
		var admin = await AdminAsync(app);
		var repository = app.Provider.GetRequiredService<IChannelRepository>();

		await channel.ScopeTo(admin).General!.Value.DeleteAsync();

		await repository.Received(1).DeleteAsync(
			Arg.Is<GeneralChannelRecord>(record => record.Name == ChannelName));
	}

	#endregion
}
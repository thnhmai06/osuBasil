using Basil.Application.Chat.Runtime;
using Basil.Application.Users.Connections;
using Basil.Application.Multiplayer.Runtime.Events;
using Basil.Application.Users.Users;
using Basil.Domain.Auth;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Chat.Tests.Runtime;

/// <summary>Keeps room-channel membership and lifetime in step with room and connection events.</summary>
public sealed class RoomChannelHandlerTests
{
	[Fact]
	public async Task Of_ForAnOpenRoom_ReturnsTheSameOpenChannelEachTime()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var channel = app.RoomChannels.Of(setup.Room);

		Assert.False(channel.IsClosed);
		Assert.Same(channel, app.RoomChannels.Of(setup.Room));
	}

	[Fact]
	public async Task Of_ForAClosedRoom_ReturnsAClosedChannelAndKeepsNone()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).CloseAsync(
			TestContext.Current.CancellationToken)).IsOk);

		var channel = app.RoomChannels.Of(setup.Room);

		Assert.True(channel.IsClosed);
		Assert.Null(app.RoomChannels.Find(setup.Room));
	}

	[Fact]
	public async Task Handle_LobbyRoomClosed_ClosesAndForgetsTheChannel()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var channel = app.RoomChannels.Of(setup.Room);
		var joined = app.RoomEvents.Events.OfType<RoomPlayerJoined>().Last();
		Assert.True((await setup.Room.ScopeTo(setup.Creator).CloseAsync(
			TestContext.Current.CancellationToken)).IsOk);
		var closed = Assert.Single(app.LobbyEvents.Events.OfType<LobbyRoomClosed>());

		await new LobbyRoomClosedHandler(app.RoomChannels).Handle(closed, TestContext.Current.CancellationToken);

		Assert.True(channel.IsClosed);
		Assert.Null(app.RoomChannels.Find(setup.Room));
		await new RoomPlayerJoinedHandler(app.RoomChannels).Handle(joined, TestContext.Current.CancellationToken);
		Assert.Null(app.RoomChannels.Find(setup.Room));
	}

	[Fact]
	public async Task Handle_LobbyRoomOpened_OpensTheChannelWithTheFirstHost()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("test room", "", false, false,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(opened.IsOk);
		var openedEvent = Assert.Single(app.LobbyEvents.Events.OfType<LobbyRoomOpened>());

		await new LobbyRoomOpenedHandler(app.RoomChannels).Handle(openedEvent, TestContext.Current.CancellationToken);

		Assert.Contains(creator, app.RoomChannels.Of(room!).Members);
	}

	[Fact]
	public async Task Handle_RoomPlayerJoined_AddsThePlayerAndReplacesItsClosedConnection()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var channel = app.RoomChannels.Of(setup.Room);
		channel.ScopeTo(setup.Player).Join();
		app.Users.Close(setup.Player, SessionEndReason.ConnectionLost);
		var replacement = (BanchoConnection)await app.LoginAsync(setup.Player.Record, ConnectionType.Osu);
		Assert.True((await setup.Room.ScopeTo(replacement).Members.JoinAsync("",
			TestContext.Current.CancellationToken)).IsOk);
		var joined = app.RoomEvents.Events.OfType<RoomPlayerJoined>().Last();

		await new RoomPlayerJoinedHandler(app.RoomChannels).Handle(joined, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(setup.Player, channel.Members);
		Assert.Contains(replacement, channel.Members);
	}

	[Fact]
	public async Task Handle_RoomPlayerLeft_PartsThePlayerButKeepsTheManager()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var channel = app.RoomChannels.Of(setup.Room);
		channel.ScopeTo(setup.Player).Join();
		channel.ScopeTo(setup.Creator).Join();
		Assert.True((await setup.Room.ScopeTo(setup.Player).Members.LeaveAsync(
			TestContext.Current.CancellationToken)).IsOk);
		var left = Assert.Single(app.RoomEvents.Events.OfType<RoomPlayerLeft>());

		await new RoomPlayerLeftHandler(app.RoomChannels).Handle(left, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(setup.Player, channel.Members);
		Assert.Contains(setup.Creator, channel.Members);
	}

	[Fact]
	public async Task Handle_RoomPlayerKicked_PartsTheRemovedPlayer()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var channel = app.RoomChannels.Of(setup.Room);
		channel.ScopeTo(setup.Player).Join();
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Members.KickAsync(setup.Player.Record,
			TestContext.Current.CancellationToken)).IsOk);
		var kicked = Assert.Single(app.RoomEvents.Events.OfType<RoomPlayerKicked>());

		await new RoomPlayerKickedHandler(app.RoomChannels).Handle(kicked, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(setup.Player, channel.Members);
	}

	[Fact]
	public async Task Handle_RoomPlayerBanned_PartsTheEvictedPlayer()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var channel = app.RoomChannels.Of(setup.Room);
		channel.ScopeTo(setup.Player).Join();
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Members.BanAsync(setup.Player.Record,
			TestContext.Current.CancellationToken)).IsOk);
		var banned = Assert.Single(app.RoomEvents.Events.OfType<RoomPlayerBanned>());

		await new RoomPlayerBannedHandler(app.RoomChannels).Handle(banned, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(setup.Player, channel.Members);
	}

	[Fact]
	public async Task Handle_RoomSlotLockChanged_PartsTheEvictedPlayer()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var channel = app.RoomChannels.Of(setup.Room);
		channel.ScopeTo(setup.Player).Join();
		var slot = setup.Room.Slots.Find(setup.Player)!;
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Slots.ToggleLockAsync(slot.Index,
			TestContext.Current.CancellationToken)).IsOk);
		var changed = Assert.Single(app.RoomEvents.Events.OfType<RoomSlotLockChanged>());

		await new RoomSlotLockChangedHandler(app.RoomChannels).Handle(changed,
			TestContext.Current.CancellationToken);

		Assert.DoesNotContain(setup.Player, channel.Members);
	}

	[Fact]
	public async Task Handle_RoomObserverJoined_AddsTheObserver()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var observerRecord = await app.CreateUserAsync("Observer", "password",
			UserPermissionsGroups.Default | UserPermissions.RoomObserve);
		var observer = (TourneyConnection)await app.LoginAsync(observerRecord, ConnectionType.Tourney);
		var channel = app.RoomChannels.Of(setup.Room);
		Assert.True((await setup.Room.ScopeTo(observer).Members.ObserverJoinAsync(
			TestContext.Current.CancellationToken)).IsOk);
		var joined = Assert.Single(app.RoomEvents.Events.OfType<RoomObserverJoined>());

		await new RoomObserverJoinedHandler(app.RoomChannels).Handle(joined, TestContext.Current.CancellationToken);

		Assert.Contains(observer, channel.Members);
	}

	[Fact]
	public async Task Handle_RoomObserverLeft_PartsTheObserver()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var observerRecord = await app.CreateUserAsync("Observer", "password",
			UserPermissionsGroups.Default | UserPermissions.RoomObserve);
		var observer = (TourneyConnection)await app.LoginAsync(observerRecord, ConnectionType.Tourney);
		Assert.True((await setup.Room.ScopeTo(observer).Members.ObserverJoinAsync(
			TestContext.Current.CancellationToken)).IsOk);
		var channel = app.RoomChannels.Of(setup.Room);
		var joined = Assert.Single(app.RoomEvents.Events.OfType<RoomObserverJoined>());
		await new RoomObserverJoinedHandler(app.RoomChannels).Handle(joined, TestContext.Current.CancellationToken);
		Assert.Contains(observer, channel.Members);
		Assert.True((await setup.Room.ScopeTo(observer).Members.ObserverLeaveAsync(
			TestContext.Current.CancellationToken)).IsOk);
		var left = Assert.Single(app.RoomEvents.Events.OfType<RoomObserverLeft>());

		await new RoomObserverLeftHandler(app.RoomChannels).Handle(left, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(observer, channel.Members);
	}

	[Fact]
	public async Task Handle_LobbyConnectionReleased_PartsTheConnectionFromEveryRoomChannel()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var channel = app.RoomChannels.Of(setup.Room);
		var otherChannel = app.RoomChannels.Of(setup.SuspendedRoom);
		channel.ScopeTo(setup.Outsider).Join();
		otherChannel.ScopeTo(setup.Outsider).Join();
		await app.Lobby.ReleaseAsync(setup.Outsider, TestContext.Current.CancellationToken);
		var released = Assert.Single(app.LobbyEvents.Events.OfType<LobbyConnectionReleased>());

		await new LobbyConnectionReleasedHandler(app.Lobby, app.RoomChannels).Handle(released,
			TestContext.Current.CancellationToken);

		Assert.DoesNotContain(setup.Outsider, channel.Members);
		Assert.DoesNotContain(setup.Outsider, otherChannel.Members);
	}
}
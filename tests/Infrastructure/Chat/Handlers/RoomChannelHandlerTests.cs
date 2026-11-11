using Basil.Application.Chat.Objects;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Basil.Domain.Users;
using Basil.Infrastructure.Chat.Handlers;
using Basil.Testing;

namespace Basil.Infrastructure.Chat.Tests.Handlers;

/// <summary>Keeps room-channel membership and lifetime in step with room and connection events.</summary>
public sealed class RoomChannelHandlerTests
{
	[Fact]
	public async Task OpenRoom_SameChannel()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var channel = app.RoomChannels.Of(setup.Room);

		Assert.False(channel.IsClosed);
		Assert.Same(channel, app.RoomChannels.Of(setup.Room));
	}

	[Fact]
	public async Task ClosedRoom_ClosedChannelKept()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True(await setup.Room.ScopeTo(setup.Creator).CloseAsync() == RoomOutcome.Ok);

		var channel = app.RoomChannels.Of(setup.Room);

		Assert.True(channel.IsClosed);
		Assert.Null(app.RoomChannels.Find(setup.Room));
	}

	[Fact]
	public async Task LobbyRoomClosed_ClosesChannel()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var channel = app.RoomChannels.Of(setup.Room);
		var joined = app.RoomEvents.Events.OfType<RoomPlayerJoined>().Last();
		Assert.True(await setup.Room.ScopeTo(setup.Creator).CloseAsync() == RoomOutcome.Ok);
		var closed = Assert.Single(app.LobbyEvents.Events.OfType<LobbyRoomClosed>());

		await new LobbyRoomClosedHandler(app.RoomChannels).Handle(closed, TestContext.Current.CancellationToken);

		Assert.True(channel.IsClosed);
		Assert.Null(app.RoomChannels.Find(setup.Room));
		await new RoomPlayerJoinedHandler(app.RoomChannels).Handle(joined, TestContext.Current.CancellationToken);
		Assert.Null(app.RoomChannels.Find(setup.Room));
	}

	[Fact]
	public async Task LobbyRoomOpened_ChannelWithHost()
	{
		using var app = new TestApplication();
		var creator = await RoomFixture.PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("test room", "", RoomSource.Game, false);
		Assert.True(opened == RoomOutcome.Ok);
		var openedEvent = Assert.Single(app.LobbyEvents.Events.OfType<LobbyRoomOpened>());

		await new LobbyRoomOpenedHandler(app.RoomChannels).Handle(openedEvent, TestContext.Current.CancellationToken);

		Assert.Contains(creator, app.RoomChannels.Of(room!).Members);
	}

	[Fact]
	public async Task RoomPlayerJoined_AddsAndReplaces()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var channel = app.RoomChannels.Of(setup.Room);
		channel.ScopeTo(setup.Player).Join();
		app.Users.Close(setup.Player, SessionEndReason.ConnectionLost);
		var replacement = (StableConnection)await app.LoginAsync(setup.Player.Record, SessionType.Stable);
		Assert.True(await setup.Room.ScopeTo(replacement).Members.JoinAsync("") == RoomOutcome.Ok);
		var joined = app.RoomEvents.Events.OfType<RoomPlayerJoined>().Last();

		await new RoomPlayerJoinedHandler(app.RoomChannels).Handle(joined, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(setup.Player, channel.Members);
		Assert.Contains(replacement, channel.Members);
	}

	[Fact]
	public async Task RoomPlayerLeft_KeepsManager()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var channel = app.RoomChannels.Of(setup.Room);
		channel.ScopeTo(setup.Player).Join();
		channel.ScopeTo(setup.Creator).Join();
		Assert.True(await setup.Room.ScopeTo(setup.Player).Members.LeaveAsync() == RoomOutcome.Ok);
		var left = Assert.Single(app.RoomEvents.Events.OfType<RoomPlayerLeft>());

		await new RoomPlayerLeftHandler(app.RoomChannels).Handle(left, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(setup.Player, channel.Members);
		Assert.Contains(setup.Creator, channel.Members);
	}

	[Fact]
	public async Task RoomPlayerKicked_PartsRemoved()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var channel = app.RoomChannels.Of(setup.Room);
		channel.ScopeTo(setup.Player).Join();
		Assert.True(await setup.Room.ScopeTo(setup.Creator).Members
			.KickAsync(UserReference.ById(setup.Player.Record.Id)) == RoomOutcome.Ok);
		var kicked = Assert.Single(app.RoomEvents.Events.OfType<RoomPlayerKicked>());

		await new RoomPlayerKickedHandler(app.RoomChannels).Handle(kicked, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(setup.Player, channel.Members);
	}

	[Fact]
	public async Task RoomPlayerBanned_PartsEvicted()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var channel = app.RoomChannels.Of(setup.Room);
		channel.ScopeTo(setup.Player).Join();
		Assert.True(
			await setup.Room.ScopeTo(setup.Creator).Members.BanAsync(UserReference.ById(setup.Player.Record.Id)) ==
			RoomOutcome.Ok);
		var banned = Assert.Single(app.RoomEvents.Events.OfType<RoomPlayerBanned>());

		await new RoomPlayerBannedHandler(app.RoomChannels).Handle(banned, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(setup.Player, channel.Members);
	}

	[Fact]
	public async Task SlotLockChanged_PartsEvicted()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var channel = app.RoomChannels.Of(setup.Room);
		channel.ScopeTo(setup.Player).Join();
		var slot = setup.Room.Slots.Find(setup.Player)!;
		Assert.True(await setup.Room.ScopeTo(setup.Creator).Slots.ToggleLockAsync(slot.Index) == RoomOutcome.Ok);
		var changed = Assert.Single(app.RoomEvents.Events.OfType<RoomSlotLockChanged>());

		await new RoomSlotLockChangedHandler(app.RoomChannels).Handle(changed,
			TestContext.Current.CancellationToken);

		Assert.DoesNotContain(setup.Player, channel.Members);
	}

	[Fact]
	public async Task RoomObserverJoined_AddsObserver()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var observerRecord = await app.CreateUserAsync("Observer", "password",
			UserPrivilegesExtensions.Default | UserPrivileges.Tournament);
		var observer = (TourneyConnection)await app.LoginAsync(observerRecord, SessionType.StableTourney);
		var channel = app.RoomChannels.Of(setup.Room);
		Assert.True(await setup.Room.ScopeTo(observer).Members.ObserverJoinAsync() == RoomOutcome.Ok);
		var joined = Assert.Single(app.RoomEvents.Events.OfType<RoomObserverJoined>());

		await new RoomObserverJoinedHandler(app.RoomChannels).Handle(joined, TestContext.Current.CancellationToken);

		Assert.Contains(observer, channel.Members);
	}

	[Fact]
	public async Task RoomObserverLeft_PartsObserver()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var observerRecord = await app.CreateUserAsync("Observer", "password",
			UserPrivilegesExtensions.Default | UserPrivileges.Tournament);
		var observer = (TourneyConnection)await app.LoginAsync(observerRecord, SessionType.StableTourney);
		Assert.True(await setup.Room.ScopeTo(observer).Members.ObserverJoinAsync() == RoomOutcome.Ok);
		var channel = app.RoomChannels.Of(setup.Room);
		var joined = Assert.Single(app.RoomEvents.Events.OfType<RoomObserverJoined>());
		await new RoomObserverJoinedHandler(app.RoomChannels).Handle(joined, TestContext.Current.CancellationToken);
		Assert.Contains(observer, channel.Members);
		Assert.True(await setup.Room.ScopeTo(observer).Members.ObserverLeaveAsync() == RoomOutcome.Ok);
		var left = Assert.Single(app.RoomEvents.Events.OfType<RoomObserverLeft>());

		await new RoomObserverLeftHandler(app.RoomChannels).Handle(left, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(observer, channel.Members);
	}

	[Fact]
	public async Task NoSlotReferee_Parted()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var channel = app.RoomChannels.Of(setup.Room);
		Assert.True(channel.ScopeTo(setup.Referee).Join() == ChannelOutcome.Ok);
		Assert.True(await setup.Room.ScopeTo(setup.Creator).Authority
			.RemoveRefereeAsync(UserReference.ById(setup.Referee.Record.Id)) == RoomOutcome.Ok);
		var removed = Assert.Single(app.RoomEvents.Events.OfType<RoomRefereeRemoved>());

		await new RoomRefereeRemovedHandler(app.RoomChannels, app.Clock).Handle(removed,
			TestContext.Current.CancellationToken);

		Assert.DoesNotContain(setup.Referee, channel.Members);
	}

	[Fact]
	public async Task SlottedReferee_Kept()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var channel = app.RoomChannels.Of(setup.Room);
		Assert.True(await setup.Room.ScopeTo(setup.Creator).Authority
			.AddRefereeAsync(UserReference.ById(setup.Player.Record.Id)) == RoomOutcome.Ok);
		Assert.True(channel.ScopeTo(setup.Player).Join() == ChannelOutcome.Ok);
		Assert.True(await setup.Room.ScopeTo(setup.Creator).Authority
			.RemoveRefereeAsync(UserReference.ById(setup.Player.Record.Id)) == RoomOutcome.Ok);
		var removed = Assert.Single(app.RoomEvents.Events.OfType<RoomRefereeRemoved>());

		await new RoomRefereeRemovedHandler(app.RoomChannels, app.Clock).Handle(removed,
			TestContext.Current.CancellationToken);

		Assert.Contains(setup.Player, channel.Members);
	}

	[Fact]
	public async Task LobbyConnectionReleased_PartsEveryChannel()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var channel = app.RoomChannels.Of(setup.Room);
		var otherChannel = app.RoomChannels.Of(setup.SuspendedRoom);
		channel.ScopeTo(setup.Outsider).Join();
		otherChannel.ScopeTo(setup.Outsider).Join();
		await app.Lobby.ReleaseAsync(setup.Outsider);
		var released = Assert.Single(app.LobbyEvents.Events.OfType<LobbyConnectionReleased>());

		await new LobbyConnectionReleasedHandler(app.Lobby, app.RoomChannels).Handle(released,
			TestContext.Current.CancellationToken);

		Assert.DoesNotContain(setup.Outsider, channel.Members);
		Assert.DoesNotContain(setup.Outsider, otherChannel.Members);
	}
}
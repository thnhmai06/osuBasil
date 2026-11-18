using Basil.Application.Shared.Events;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Basil.Domain.Client;
using Basil.Domain.Users;

namespace Basil.Application.Users.Objects.Events;

public abstract record UserEvent : Event;

public sealed record UserConnectionOpened(Connection Connection, bool CameOnline) : UserEvent;

public sealed record UserConnectionClosed(Connection Connection, SessionEndReason Reason, bool WentOffline)
	: UserEvent;

public sealed record UserConnectionStatusChanged(StableConnection Connection, PlayerStatus Status) : UserEvent;

public sealed record UserSilenced(UserRecord User, DateTimeOffset Until) : UserEvent;

public sealed record UserUnsilenced(UserRecord User) : UserEvent;

public sealed record UserPrivilegesChanged(UserRecord User, UserPrivileges Privileges) : UserEvent;

public sealed record UserNotificationSent(IReadOnlyList<StableConnection> Recipients, string Text) : UserEvent;

public abstract record UserSpectatorEvent(StableConnection Host, Connection Spectator) : UserEvent;

public sealed record UserSpectatorJoined(StableConnection Host, Connection Spectator)
	: UserSpectatorEvent(Host, Spectator);

public sealed record UserSpectatorLeft(StableConnection Host, Connection Spectator)
	: UserSpectatorEvent(Host, Spectator);

public sealed record UserSpectatorFramesReceived(StableConnection Host, SpectatorBundle Bundle) : UserEvent;

public sealed record UserSpectatorFailed(StableConnection Host, Connection Spectator)
	: UserSpectatorEvent(Host, Spectator);
using Basil.Application.Shared.Events;
using Basil.Domain.Users;

namespace Basil.Application.Users.Objects.Events;

[EventLane(EventLane.Presence)]
public abstract record UserEvent : Event;

public sealed record UserSilenced(UserRecord User, DateTimeOffset Until) : UserEvent;

public sealed record UserUnsilenced(UserRecord User) : UserEvent;

public sealed record UserPrivilegesChanged(UserRecord User, UserPrivileges Privileges) : UserEvent;

public sealed record UserDeleted(UserRecord User) : UserEvent;
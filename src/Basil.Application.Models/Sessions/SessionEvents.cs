using Basil.Domain.Events;

namespace Basil.Application.Models.Sessions;

/// <summary>A game session's reported presence status changed.</summary>
public sealed record StatusChanged(GameSession Session) : Event;

/// <summary>A game session started spectating another.</summary>
public sealed record SpectateStarted(GameSession Spectator, GameSession Host) : Event;

/// <summary>A game session stopped spectating another.</summary>
public sealed record SpectateStopped(GameSession Spectator, GameSession Host) : Event;

/// <summary>A session joined a channel.</summary>
public sealed record ChannelJoined(ChannelSession Channel, UserSession Session) : Event;

/// <summary>A session parted a channel.</summary>
public sealed record ChannelParted(ChannelSession Channel, UserSession Session) : Event;
using Basil.Domain.Users;

using Basil.Application.Common.Notifications;
using Basil.Application.Multiplayer.Events;
namespace Basil.Application.Multiplayer;

/// <summary>A notification about a room the recipient is in.</summary>
public abstract record RoomNotification : Notification;

/// <summary>A notification that a room's settings or authority changed.</summary>
public abstract record RoomSettingsNotification : RoomNotification;

/// <summary>A notification about a round in the recipient's room.</summary>
public abstract record RoomRoundNotification : RoomNotification;

/// <summary>A notification about joining, leaving, or being invited to a room.</summary>
public abstract record RoomMembershipNotification : RoomNotification;

/// <summary>A room's settings or slots changed.</summary>
public sealed record RoomUpdated(Room Room) : RoomSettingsNotification;

/// <summary>A room's host changed.</summary>
public sealed record HostTransferred : RoomSettingsNotification;

/// <summary>A room countdown ticked past a milestone.</summary>
public sealed record CountdownTick(TimeSpan Remaining) : RoomRoundNotification;

/// <summary>A round started in a room.</summary>
public sealed record RoundStarted(Room Room) : RoomRoundNotification;

/// <summary>The current round in the recipient's room was aborted.</summary>
public sealed record RoundAborted : RoomRoundNotification;

/// <summary>The current round in the recipient's room completed.</summary>
public sealed record RoundCompleted : RoomRoundNotification;

/// <summary>Every playing player in the recipient's room finished loading the beatmap.</summary>
public sealed record AllPlayersLoaded : RoomRoundNotification;

/// <summary>Every playing player in the recipient's room skipped the current beatmap's intro.</summary>
public sealed record AllPlayersSkipped : RoomRoundNotification;

/// <summary>A player in the recipient's room skipped the current beatmap's intro.</summary>
/// <param name="Slot">The slot of the player who skipped.</param>
public sealed record PlayerSkipped(RoomSlot Slot) : RoomRoundNotification;

/// <summary>A player in the recipient's room failed the current round.</summary>
/// <param name="Slot">The slot of the player who failed.</param>
public sealed record PlayerFailed(RoomSlot Slot) : RoomRoundNotification;

/// <summary>The recipient joined a room.</summary>
public sealed record RoomJoined(Room Room) : RoomMembershipNotification;

/// <summary>A room closed.</summary>
/// <param name="Room">The room that closed.</param>
public sealed record RoomClosed(Room Room) : RoomMembershipNotification;

/// <summary>The recipient's attempt to join a room was refused.</summary>
public sealed record RoomJoinRefused : RoomMembershipNotification;

/// <summary>The recipient was invited to a room.</summary>
/// <param name="Room">The room the recipient was invited to.</param>
/// <param name="From">The user who extended the invitation, or <see langword="null" /> when unknown.</param>
public sealed record Invited(Room Room, User? From) : RoomMembershipNotification;

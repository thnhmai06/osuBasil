using Basil.Application.Models.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;

namespace Basil.Application.Models.Events.Multiplayer;

/// <summary>A room's shared settings changed.</summary>
public abstract record RoomSettingsEvent(Room Room) : RoomEvent(Room);

/// <summary>The room's name changed.</summary>
public sealed record RoomNameChanged(Room Room, string Name) : RoomSettingsEvent(Room);

/// <summary>The room's public visibility changed.</summary>
public sealed record RoomVisibilityChanged(Room Room, bool IsVisible) : RoomSettingsEvent(Room);

/// <summary>The room's password changed.</summary>
public sealed record RoomPasswordChanged(Room Room, string Password) : RoomSettingsEvent(Room);

/// <summary>The room's selected beatmap changed.</summary>
public sealed record BeatmapChanged(Room Room, Beatmap? Beatmap) : RoomSettingsEvent(Room);

/// <summary>The room's game mode changed.</summary>
public sealed record GameModeChanged(Room Room, GameMode Mode) : RoomSettingsEvent(Room);

/// <summary>The room's global mods changed.</summary>
public sealed record ModsChanged(Room Room, GameMods Mods) : RoomSettingsEvent(Room);

/// <summary>The room's freemod setting changed.</summary>
public sealed record FreemodsChanged(Room Room, bool Enabled) : RoomSettingsEvent(Room);

/// <summary>The room's team arrangement changed, including any automatic team reassignments.</summary>
public sealed record TeamTypeChanged(Room Room, GameTeamType TeamType) : RoomSettingsEvent(Room);

/// <summary>The room's win condition changed.</summary>
public sealed record WinConditionChanged(Room Room, GameWinCondition WinCondition) : RoomSettingsEvent(Room);
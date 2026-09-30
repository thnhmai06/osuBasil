using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;

using Basil.Application.Multiplayer;
namespace Basil.Application.Multiplayer.Events;

/// <summary>A room's shared settings changed.</summary>
public abstract record RoomSettingsEvent(Room Room) : RoomEvent(Room);

/// <summary>The room's name changed.</summary>
public sealed record RoomNameChanged(Room Room, string Name) : RoomSettingsEvent(Room);

/// <summary>The room's history privacy changed.</summary>
public sealed record RoomPrivacyChanged(Room Room, bool IsPrivate) : RoomSettingsEvent(Room);

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
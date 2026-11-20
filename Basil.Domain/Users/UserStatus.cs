using Basil.Domain.Beatmaps;
using Basil.Domain.Game;

namespace Basil.Domain.Users;

/// <summary>What a user is doing and, when it concerns a beatmap, which one.</summary>
public sealed class UserStatus
{
	/// <summary>The kind of activity; it starts as <see cref="UserActivity.Idle" />.</summary>
	public UserActivity Activity { get; init; } = UserActivity.Idle;

	/// <summary>The beatmap, mode and mods the activity concerns, or <see langword="null" /> when it concerns none.</summary>
	public UserStatusDetail? Detail { get; set; }
}

/// <summary>The beatmap, game mode and mods a user's activity concerns.</summary>
/// <param name="BeatmapReference">The beatmap the user is on.</param>
/// <param name="Mode">The game mode the user is playing in.</param>
/// <param name="Mods">The mods the user has selected.</param>
public readonly record struct UserStatusDetail(BeatmapReference BeatmapReference, GameMode Mode, GameMods Mods);
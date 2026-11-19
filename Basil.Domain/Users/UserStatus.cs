using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;

namespace Basil.Domain.Users;

public sealed class UserStatus
{
	public UserActivity Activity { get; init; } = UserActivity.Idle;

	public UserStatusDetail? Detail { get; set; }
}

public readonly record struct UserStatusDetail(BeatmapReference BeatmapReference, GameMode Mode, GameMods Mods);
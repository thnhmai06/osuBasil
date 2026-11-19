using Basil.Domain.Mechanics;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Users;

public sealed class UserStatus
{
	public UserActivity Activity { get; init; } = UserActivity.Idle;

	public UserStatusDetail? Detail { get; set; }
}

public readonly record struct UserStatusDetail
{
	public Md5Hash BeatmapHash { get; init; }

	public string BeatmapFullName { get; init; }

	public int? BeatmapId { get; init; }

	public GameMode GameMode { get; init; }

	public GameMods GameMods { get; init; }
}
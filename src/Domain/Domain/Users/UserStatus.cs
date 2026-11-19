using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Users;

public sealed record UserStatus(
	UserActivity Activity,
	string Text,
	Md5Hash? BeatmapHash,
	int? BeatmapId,
	GameMods GameMods,
	GameMode GameMode)
{
	public static readonly UserStatus Idle =
		new(UserActivity.Idle, string.Empty, null, null, GameMods.NoMod, GameMode.Standard);
}
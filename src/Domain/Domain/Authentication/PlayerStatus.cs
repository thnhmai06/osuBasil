using Basil.Domain.Game.Mechanics;
using Basil.Domain.Game.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Authentication;

public sealed record PlayerStatus(
	UserActivity Activity,
	string Text,
	Md5Hash? BeatmapHash,
	int? BeatmapId,
	GameMods GameMods,
	GameMode GameMode)
{
	public static readonly PlayerStatus Idle =
		new(UserActivity.Idle, string.Empty, null, null, GameMods.NoMod, GameMode.Standard);
}
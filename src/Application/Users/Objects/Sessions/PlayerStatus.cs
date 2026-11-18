
using Basil.Domain.Game.Mechanics;
using Basil.Domain.Users;
using Basil.Domain.Game.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Users.Objects.Sessions;

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
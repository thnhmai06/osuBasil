using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Application.Users.Objects.Connections;

public sealed record PlayerStatus(
	UserActivity Activity,
	string Text,
	Md5? BeatmapHash,
	int? BeatmapId,
	GameMods GameMods,
	GameMode GameMode)
{
	public static readonly PlayerStatus Idle =
		new(UserActivity.Idle, string.Empty, null, null, GameMods.NoMod, GameMode.Standard);
}
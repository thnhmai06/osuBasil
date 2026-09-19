using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer.Runtime;

namespace Basil.Domain.Multiplayer.Records;

public sealed class MatchSettings : RoomSettings
{
	public new required Beatmap Beatmap
	{
		get => base.Beatmap!;
		init => base.Beatmap = value;
	}
}
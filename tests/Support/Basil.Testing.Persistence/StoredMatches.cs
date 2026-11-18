using Basil.Domain.Game.Mechanics;
using Basil.Domain.Multiplayer;

namespace Basil.Testing.Persistence;

public static class StoredMatches
{
	public static Md5 BeatmapHash => new("00000000000000000000000000000000");

	public static MatchData Data(string name, DateTimeOffset startedAt, UserRecord creator,
		DateTimeOffset? endedAt = null, bool isPrivate = false)
	{
		return new MatchData
		{
			Name = name,
			Creator = creator,
			StartedAt = startedAt,
			EndedAt = endedAt,
			IsPrivate = isPrivate
		};
	}

	public static Round Round(MatchRecord match, int index, DateTimeOffset startedAt, Md5? beatmapHash = null,
		MatchSettings? settings = null)
	{
		return new Round
		{
			Match = match,
			Index = index,
			BeatmapHash = beatmapHash ?? BeatmapHash,
			Settings = settings ?? new MatchSettings(),
			StartedAt = startedAt,
			EndedAt = null
		};
	}

	public static Round TeamRound(MatchRecord match, int index, DateTimeOffset startedAt)
	{
		return Round(match, index, startedAt, settings: new MatchSettings { TeamType = GameTeamType.TeamVs });
	}

	public static MatchEvent Event(MatchRecord match, MatchEventType type, DateTimeOffset timestamp,
		UserRecord? actor = null, UserRecord? target = null, string? detail = null)
	{
		return new MatchEvent(match, type, timestamp, actor, target, detail);
	}
}
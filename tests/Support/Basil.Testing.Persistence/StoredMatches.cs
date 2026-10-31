using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Testing.Persistence;

/// <summary>Builds the match records the persistence tests work on, with only the parts they name.</summary>
public static class StoredMatches
{
	/// <summary>The hash of the beatmap a round is played on, when the test names none.</summary>
	public static Md5 BeatmapHash => new("00000000000000000000000000000000");

	/// <summary>Creates the data of a match.</summary>
	/// <param name="name">The name of the match.</param>
	/// <param name="startedAt">The moment the match started.</param>
	/// <param name="creator">The account that created the match.</param>
	/// <param name="endedAt">The moment the match ended, or <see langword="null" /> while it runs.</param>
	/// <param name="isPrivate">Whether the match history is visible only to its creator and participants.</param>
	/// <returns>The match's data, as a commit stores it.</returns>
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

	/// <summary>Creates a round of a match.</summary>
	/// <param name="match">The match the round belongs to.</param>
	/// <param name="index">The number of the round within its match.</param>
	/// <param name="startedAt">The moment the round started.</param>
	/// <param name="beatmapHash">The hash of the beatmap the round is played on.</param>
	/// <param name="settings">The settings the round is played under.</param>
	/// <returns>The round.</returns>
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

	/// <summary>Creates a round played in two opposing teams.</summary>
	/// <param name="match">The match the round belongs to.</param>
	/// <param name="index">The number of the round within its match.</param>
	/// <param name="startedAt">The moment the round started.</param>
	/// <returns>The round.</returns>
	public static Round TeamRound(MatchRecord match, int index, DateTimeOffset startedAt)
	{
		return Round(match, index, startedAt, settings: new MatchSettings { TeamType = GameTeamType.TeamVs });
	}

	/// <summary>Creates an event of a match.</summary>
	/// <param name="match">The match the event belongs to.</param>
	/// <param name="type">The kind of the event.</param>
	/// <param name="timestamp">The moment the event happened.</param>
	/// <param name="actor">The account that performed the action, or <see langword="null" />.</param>
	/// <param name="target">The account the action affected, or <see langword="null" />.</param>
	/// <param name="detail">The detail of the event, or <see langword="null" />.</param>
	/// <returns>The event.</returns>
	public static MatchEvent Event(MatchRecord match, MatchEventType type, DateTimeOffset timestamp,
		UserRecord? actor = null, UserRecord? target = null, string? detail = null)
	{
		return new MatchEvent(match, type, timestamp, actor, target, detail);
	}
}
namespace Basil.Protocol.Game.Stable.Models.Web;

/// <summary>The leaderboard of a beatmap as the client lists it.</summary>
/// <param name="RankedStatus">The ranked status number of the beatmapset.</param>
/// <param name="BeatmapId">The beatmap id.</param>
/// <param name="BeatmapsetId">The beatmapset id.</param>
/// <param name="BeatmapName">The full name of the beatmap shown above the list.</param>
/// <param name="Rows">The scores, best first.</param>
public sealed record LeaderboardReply(
	int RankedStatus,
	int BeatmapId,
	int BeatmapsetId,
	string BeatmapName,
	IReadOnlyList<LeaderboardRow> Rows);

/// <summary>One score of a leaderboard.</summary>
public sealed record LeaderboardRow(
	long ScoreId,
	string PlayerName,
	long TotalScore,
	int MaxCombo,
	int Num50,
	int Num100,
	int Num300,
	int NumMiss,
	int NumKatu,
	int NumGeki,
	bool FullCombo,
	int Mods,
	int PlayerId,
	int Rank,
	long UnixTime);

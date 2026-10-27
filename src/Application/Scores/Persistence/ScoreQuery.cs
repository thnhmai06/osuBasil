using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Persistence;

/// <summary>Which scores a score listing includes.</summary>
/// <param name="Player">The user who set the scores, or <see langword="null" /> for any.</param>
/// <param name="BeatmapHash">The beatmap the scores were set on, or <see langword="null" /> for any.</param>
/// <param name="Mods">The exact mods applied to the play, or <see langword="null" /> for any.</param>
/// <param name="Players">The players whose scores are included, or <see langword="null" /> for any.</param>
/// <param name="BestPerPlayer">Whether to include only each player's highest-scoring play.</param>
/// <remarks>
///     A global leaderboard uses <see cref="BeatmapHash" /> and <see cref="BestPerPlayer" />; a mods leaderboard
///     also uses <see cref="Mods" />; friends and country leaderboards also use <see cref="Players" />.
/// </remarks>
public sealed record ScoreQuery(
	UserRecord? Player = null,
	Md5? BeatmapHash = null,
	GameMods? Mods = null,
	IReadOnlySet<UserRecord>? Players = null,
	bool BestPerPlayer = false);
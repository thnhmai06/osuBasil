using Basil.Domain.Beatmaps;
using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Mediator;

namespace Basil.Application.Scores.Messages;

/// <summary>Asks Scores for the best scores of a beatmap.</summary>
/// <param name="BeatmapHash">The hash of the beatmap file.</param>
/// <param name="Mode">The game mode of the scores.</param>
/// <param name="Mods">Only the scores played with exactly these mods, or <see langword="null" /> for any.</param>
/// <remarks>The answer is <see langword="null" /> when the server knows no such beatmap. Scores are public.</remarks>
public sealed record GetLeaderboard(Md5 BeatmapHash, GameMode Mode, GameMods? Mods) : IRequest<Leaderboard?>;

/// <summary>The best score of each player on a beatmap, best first.</summary>
/// <param name="Beatmap">The beatmap.</param>
/// <param name="Entries">The scores with the name of each player.</param>
public sealed record Leaderboard(BeatmapRecord Beatmap, IReadOnlyList<LeaderboardEntry> Entries);

/// <summary>A score on a leaderboard.</summary>
/// <param name="Score">The score.</param>
/// <param name="PlayerName">The name of the player, or <c>?</c> when the player is unknown.</param>
public sealed record LeaderboardEntry(ScoreRecord Score, string PlayerName);
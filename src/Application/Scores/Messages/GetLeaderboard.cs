using Basil.Domain.Beatmaps;
using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Mediator;

namespace Basil.Application.Scores.Messages;

public sealed record GetLeaderboard(Md5 BeatmapHash, GameMode Mode, GameMods? Mods) : IRequest<Leaderboard?>;

public sealed record Leaderboard(BeatmapRecord Beatmap, IReadOnlyList<LeaderboardEntry> Entries);

public sealed record LeaderboardEntry(ScoreRecord Score, string PlayerName);
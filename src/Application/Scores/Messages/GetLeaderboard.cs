using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Shared.Structures.Cryptography;
using Mediator;

namespace Basil.Application.Scores.Messages;

public sealed record GetLeaderboard(Md5Hash BeatmapHash, GameMode Mode, GameMods? Mods) : IRequest<Leaderboard?>;

public sealed record Leaderboard(BeatmapRecord Beatmap, IReadOnlyList<LeaderboardEntry> Entries);

public sealed record LeaderboardEntry(ScoreRecord Score, string PlayerName);
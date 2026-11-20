using Basil.Domain.Game;
using Basil.Domain.Scores;
using Basil.Shared.Structures.Cryptography;
using Mediator;

namespace Basil.Application.Rulesets.Messages;

/// <summary>Gets the star rating of a beatmap under mods; null when it cannot be calculated.</summary>
public sealed record GetBeatmapStars(int BeatmapId, Md5Hash Checksum, GameMode Mode, GameMods Mods)
	: IRequest<double?>;

/// <summary>Queues the calculation of a beatmap and returns at once; <c>BeatmapCalculated</c> reports the result.</summary>
public sealed record CalculateBeatmap(int BeatmapId, Md5Hash Checksum, GameMode Mode) : IRequest;

/// <summary>Queues the calculation of the pp of a play and returns at once; <c>ScorePerformanceCalculated</c> reports the result under <paramref name="Key"/>.</summary>
public sealed record CalculateScorePerformance(
	Guid Key, int BeatmapId, Md5Hash Checksum, GameMode Mode, GameMods Mods, HitCounts Hits, int MaxCombo)
	: IRequest;
using Basil.Domain.Game;
using Basil.Domain.Scores;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Rulesets.Ports;

internal interface IBeatmapCalculator
{
	/// <summary>Calculates the star rating of a beatmap; null when the calculation stack is not available.</summary>
	Task<double?> GetStarsAsync(int beatmapId, Md5Hash checksum, GameMode mode, GameMods mods,
		CancellationToken ct = default);

	/// <summary>Calculates the pp of a play; null when the calculation stack is not available.</summary>
	Task<double?> GetPerformanceAsync(int beatmapId, Md5Hash checksum, GameMode mode, GameMods mods, HitCounts hits,
		int combo, CancellationToken ct = default);
}
using Basil.Domain.Game;
using Basil.Domain.Scores;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Rulesets.Ports;

internal interface IBeatmapCalculator
{
	Task<double> GetStarsAsync(int beatmapId, Md5Hash checksum, GameMode mode, GameMods mods,
		CancellationToken ct = default);

	Task<double> GetPerformanceAsync(int beatmapId, Md5Hash checksum, GameMode mode, GameMods mods, HitCounts hits,
		int combo, CancellationToken ct = default);
}
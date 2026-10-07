using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Storage.Caching;

namespace Basil.Infrastructure.Storage.Multiplayer;

/// <summary>Keeps the current report for each match.</summary>
internal sealed class MatchReportCache
{
	internal IdentityMap<int, MatchReport> Reports { get; } = new();

	public void Invalidate(int matchId) => Reports.Remove(matchId);
}

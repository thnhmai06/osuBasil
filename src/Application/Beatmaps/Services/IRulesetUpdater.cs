namespace Basil.Application.Beatmaps.Services;

public sealed record RulesetVersions(string Active, string? Staged);

public sealed record RulesetCheck(RulesetVersions Versions, string Latest)
{
	public bool UpdateAvailable => Latest != (Versions.Staged ?? Versions.Active);
}

public sealed record RulesetStageResult(string Version, bool Staged);

public interface IRulesetUpdater
{
	RulesetVersions Version();

	Task<RulesetCheck> CheckAsync(CancellationToken ct = default);

	Task<RulesetStageResult> StageAsync(CancellationToken ct = default);
}

public sealed class RulesetUpdateException(string message, Exception? inner = null) : Exception(message, inner);
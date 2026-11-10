namespace Basil.Application.Beatmaps.Services;

/// <summary>The versions of the osu! rulesets that compute star ratings.</summary>
/// <param name="Active">The version in use by the running server.</param>
/// <param name="Staged">The version downloaded and waiting for a restart, or <see langword="null" /> when none waits.</param>
public sealed record RulesetVersions(string Active, string? Staged);

/// <summary>What looking for a newer version of the rulesets found.</summary>
/// <param name="Versions">The versions the server has.</param>
/// <param name="Latest">The newest stable version the feed offers.</param>
public sealed record RulesetCheck(RulesetVersions Versions, string Latest)
{
	/// <summary>Gets a value that indicates whether the feed offers a version the server neither uses nor has staged.</summary>
	public bool UpdateAvailable => Latest != (Versions.Staged ?? Versions.Active);
}

/// <summary>The result of staging the newest rulesets.</summary>
/// <param name="Version">The version that is staged, or the active one when nothing newer exists.</param>
/// <param name="Staged"><see langword="true" /> when a new version was downloaded and staged by this call.</param>
public sealed record RulesetStageResult(string Version, bool Staged);

/// <summary>Finds and stages new versions of the osu! rulesets; a staged version takes effect after a restart.</summary>
public interface IRulesetUpdater
{
	/// <summary>Gets the active and the staged version.</summary>
	RulesetVersions Version();

	/// <summary>Asks the feed for the newest stable version without downloading it.</summary>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <exception cref="RulesetUpdateException">The feed could not be read.</exception>
	Task<RulesetCheck> CheckAsync(CancellationToken ct = default);

	/// <summary>Downloads the newest stable version, verifies it against the hash the feed publishes, and stages it.</summary>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>What was staged.</returns>
	/// <remarks>When any package fails verification, nothing is staged and nothing of it is kept.</remarks>
	/// <exception cref="RulesetUpdateException">The feed could not be read or a package failed verification.</exception>
	Task<RulesetStageResult> StageAsync(CancellationToken ct = default);
}

/// <summary>A new version of the rulesets could not be found or staged.</summary>
/// <param name="message">What went wrong.</param>
/// <param name="inner">The cause, if any.</param>
public sealed class RulesetUpdateException(string message, Exception? inner = null) : Exception(message, inner);
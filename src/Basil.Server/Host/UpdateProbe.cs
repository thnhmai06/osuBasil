namespace Basil.Server.Host;

/// <summary>What a check for a newer release found.</summary>
internal enum UpdateCheckOutcome
{
	/// <summary>The server is running the newest release the feed offers.</summary>
	UpToDate,

	/// <summary>A newer release exists. Nothing has been downloaded or installed.</summary>
	UpdateAvailable,

	/// <summary>The feed could not be reached or did not answer.</summary>
	Failed,

	/// <summary>
	///     This copy was not installed by the updater, so it has no release feed to compare against.
	/// </summary>
	NotInstallable
}

/// <summary>The result of one check, and the version it found when there was one.</summary>
/// <param name="Outcome">What the check found.</param>
/// <param name="AvailableVersion">
///     The newer version, when <paramref name="Outcome" /> is
///     <see cref="UpdateCheckOutcome.UpdateAvailable" />; otherwise <see langword="null" />.
/// </param>
internal readonly record struct UpdateCheckResult(UpdateCheckOutcome Outcome, string? AvailableVersion = null);

/// <summary>Looks up whether a newer release of the server exists.</summary>
/// <remarks>
///     Checking never installs anything. Applying an update is a separate, explicit action taken
///     only by the <c>--update</c> command.
/// </remarks>
internal interface IUpdateProbe
{
	/// <summary>The version this copy of the server is running.</summary>
	string CurrentVersion { get; }

	/// <summary>Asks the release feed whether a newer version exists.</summary>
	/// <param name="cancellationToken">Cancels the lookup.</param>
	/// <returns>What the check found. A feed that cannot be reached is a result, not an exception.</returns>
	Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken);

	/// <summary>Downloads the newer release and restarts into it.</summary>
	/// <param name="cancellationToken">Cancels the download. The restart itself cannot be cancelled.</param>
	/// <returns>
	///     <see langword="false" /> when there was nothing to apply; otherwise the process is
	///     replaced and this never returns.
	/// </returns>
	Task<bool> ApplyAsync(CancellationToken cancellationToken);
}

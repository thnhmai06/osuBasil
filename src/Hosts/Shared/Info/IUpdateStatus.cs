namespace Basil.Host.Shared.Info;

/// <summary>How far a check for a newer release has got.</summary>
public enum UpdateState
{
	/// <summary>No check has finished yet.</summary>
	NotChecked,

	/// <summary>The running version is the newest.</summary>
	UpToDate,

	/// <summary>A newer version exists.</summary>
	Available,

	/// <summary>The check was not made, because it is switched off or the server was not installed by the updater.</summary>
	Skipped,

	/// <summary>The release feed could not be reached.</summary>
	Failed
}

/// <summary>The result of the last check for newer releases of Basil and of the osu! rulesets.</summary>
/// <param name="Basil">What the check found for Basil.</param>
/// <param name="BasilVersion">The newer version of Basil, when <paramref name="Basil" /> is <see cref="UpdateState.Available" />.</param>
/// <param name="Rulesets">What the check found for the osu! rulesets.</param>
/// <param name="RulesetsVersion">The newer version of the rulesets, when <paramref name="Rulesets" /> is available.</param>
public sealed record UpdateReport(
	UpdateState Basil,
	string? BasilVersion,
	UpdateState Rulesets,
	string? RulesetsVersion);

/// <summary>Tells whether newer releases exist and checks again on request; it never installs anything.</summary>
public interface IUpdateStatus
{
	/// <summary>Gets the result of the last check.</summary>
	UpdateReport Status { get; }

	/// <summary>Gets when the last check finished, or <see langword="null" /> when none has.</summary>
	DateTimeOffset? LastCheck { get; }

	/// <summary>Checks for newer releases now, even when checking on startup is switched off.</summary>
	/// <param name="ct">A token that cancels the check.</param>
	/// <returns>A task that completes when <see cref="Status" /> holds the result.</returns>
	Task CheckNowAsync(CancellationToken ct = default);
}

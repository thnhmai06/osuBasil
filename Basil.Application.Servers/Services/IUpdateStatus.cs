namespace Basil.Application.Servers.Services;

/// <summary>How far the check for a newer release of one component got.</summary>
public enum UpdateState
{
	NotChecked,

	UpToDate,

	Available,

	Skipped,

	Failed
}

/// <summary>What the last check found for Basil itself and for the osu! calculation stack.</summary>
public sealed record UpdateReport(
	UpdateState Basil,
	string? BasilVersion,
	UpdateState Rulesets,
	string? RulesetsVersion);

/// <summary>Reports whether newer releases exist and checks again on request.</summary>
public interface IUpdateStatus
{
	/// <summary>What the last check found.</summary>
	UpdateReport Status { get; }

	/// <summary>When the last check finished, or <see langword="null"/> when none has.</summary>
	DateTimeOffset? LastCheck { get; }

	/// <summary>Checks both components now and updates <see cref="Status"/>.</summary>
	Task CheckNowAsync(CancellationToken ct = default);
}

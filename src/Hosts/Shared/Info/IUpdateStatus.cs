namespace Basil.Host.Shared.Info;

public enum UpdateState
{
	NotChecked,

	UpToDate,

	Available,

	Skipped,

	Failed
}

public sealed record UpdateReport(
	UpdateState Basil,
	string? BasilVersion,
	UpdateState Rulesets,
	string? RulesetsVersion);

public interface IUpdateStatus
{
	UpdateReport Status { get; }

	DateTimeOffset? LastCheck { get; }

	Task CheckNowAsync(CancellationToken ct = default);
}
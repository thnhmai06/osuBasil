using NBomber.Contracts.Stats;

namespace Basil.LoadTests.Infrastructure.Reporting;

/// <summary>Maps the profile's plain-string report format list onto NBomber's own <see cref="ReportFormat" />.</summary>
public static class NBomberReportOptions
{
	/// <summary>
	///     Parses format names (e.g. <c>"Html"</c>, <c>"Csv"</c>) into <see cref="ReportFormat" /> values, skipping
	///     unknown ones.
	/// </summary>
	public static ReportFormat[] Parse(IEnumerable<string> formats)
	{
		return
		[
			.. formats
				.Select(f => Enum.TryParse<ReportFormat>(f, true, out var parsed) ? (ReportFormat?)parsed : null)
				.Where(f => f.HasValue)
				.Select(f => f!.Value)
		];
	}
}
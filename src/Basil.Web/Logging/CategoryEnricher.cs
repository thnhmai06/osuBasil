using Serilog.Core;
using Serilog.Events;

namespace Basil.Web.Logging;

/// <summary>
///     Tags each log event with a fixed "Category" property inferred from its SourceContext (the
///     full class name <c>ILogger&lt;T&gt;</c> attaches automatically) — a static subsystem label, not
///     a per-operation correlation id. Exact matches are checked before prefix matches so a specific
///     class (e.g. the two Background watchers) never falls through to a broader namespace rule.
/// </summary>
public sealed class CategoryEnricher : ILogEventEnricher
{
	private static readonly (string Match, bool IsPrefix, string Category)[] Rules =
	[
		("Basil.Infrastructure.Beatmaps.BeatmapWatcherService", false, "Background"),
		("Basil.Infrastructure.Beatmaps.MapsetGarbageCollectorService", false, "Background"),
		("Basil.Infrastructure.Persistence.Repositories.", true, "Database"),
		("Basil.Infrastructure.Caching.", true, "Cache"),
		("Microsoft.Hosting.Lifetime", false, "Host"),
		("Basil.Web.Program", false, "Host")
	];

	public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
	{
		if (!logEvent.Properties.TryGetValue("SourceContext", out var value) ||
		    value is not ScalarValue { Value: string sourceContext })
			return;

		foreach (var (match, isPrefix, category) in Rules)
		{
			var matched = isPrefix
				? sourceContext.StartsWith(match, StringComparison.Ordinal)
				: sourceContext == match;
			if (!matched) continue;

			logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Category", category));
			return;
		}
	}
}
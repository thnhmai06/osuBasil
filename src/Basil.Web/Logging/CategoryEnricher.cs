using Serilog.Core;
using Serilog.Events;

namespace Basil.Web.Logging;

/// <summary>
///     Tags each log event with a fixed "Category" property inferred from its SourceContext (the
///     full class name <c>ILogger&lt;T&gt;</c> attaches automatically) — a static subsystem label, not
///     a per-operation correlation id. Exact matches are checked before prefix matches so a specific
///     class never falls through to a broader namespace rule. Anything matching none of these rules
///     falls back to <see cref="FallbackCategory" /> — always set, never left blank, so
///     <c>[{Category}]</c> in the output template never renders as an empty bracket pair.
/// </summary>
public sealed class CategoryEnricher : ILogEventEnricher
{
	/// <summary>
	///     Category for every SourceContext that matches no rule below — also the marker
	///     <c>Program.ConfigureSerilog</c>'s noise filter uses to demote unclassified chatter to
	///     Warning+ only, since it isn't one of the domain scopes worth showing at Information by
	///     default.
	/// </summary>
	public const string FallbackCategory = "App";

	private static readonly (string Match, bool IsPrefix, string Category)[] Rules =
	[
		("Basil.Infrastructure.Beatmaps.", true, "Mapsets"),
		("Basil.Application.Services.Multiplayer.", true, "Matches"),
		("Basil.Application.PacketHandlers.Multiplayer.", true, "Matches"),
		("Basil.Application.Services.Scores.", true, "Scores"),
		("Basil.Application.Services.Authentication.LoginService", false, "Online"),
		("Basil.Application.Sessions.PlayerLogoutService", false, "Online"),
		("Basil.Application.Services.Irc.", true, "IRC"),
		("Basil.Infrastructure.Irc.", true, "IRC"),
		("Basil.Application.Sessions.Irc.", true, "IRC"),
		("Basil.Infrastructure.Persistence.Repositories.", true, "Database"),
		("Basil.Infrastructure.Caching.", true, "Cache"),
		("Microsoft.Hosting.Lifetime", false, "Host"),
		("Basil.Web.Program", false, "Host")
	];

	public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
	{
		var category = FallbackCategory;

		if (logEvent.Properties.TryGetValue("SourceContext", out var value) &&
		    value is ScalarValue { Value: string sourceContext })
			foreach (var (match, isPrefix, ruleCategory) in Rules)
			{
				var matched = isPrefix
					? sourceContext.StartsWith(match, StringComparison.Ordinal)
					: sourceContext == match;
				if (!matched) continue;

				category = ruleCategory;
				break;
			}

		logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Category", category));
	}
}
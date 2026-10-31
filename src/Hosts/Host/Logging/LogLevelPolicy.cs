using Basil.Host.Configuration;
using Microsoft.Extensions.Options;

namespace Basil.Host.Logging;

/// <summary>
///     Decides whether a log event is written: the lowest level of the longest override that names the source, or the
///     minimum level, read from the configuration each time so that a change applies at once.
/// </summary>
/// <param name="options">The logging settings.</param>
public sealed class LogLevelPolicy(IOptionsMonitor<LoggingOptions> options)
{
	/// <summary>Tells whether an event is written.</summary>
	/// <param name="category">The source of the event, usually the name of the logging class.</param>
	/// <param name="level">The level of the event.</param>
	/// <returns><see langword="true" /> when the event is at least as serious as the lowest level of its source.</returns>
	public bool Enabled(string? category, LogLevel level)
	{
		var current = options.CurrentValue;
		var minimum = current.MinimumLevel;
		var length = -1;
		if (category is not null)
			foreach (var (name, overridden) in current.Overrides)
				if (name.Length > length && Names(name, category))
				{
					minimum = overridden;
					length = name.Length;
				}

		return level != LogLevel.None && level >= minimum;
	}

	private static bool Names(string name, string category)
	{
		return category.StartsWith(name, StringComparison.Ordinal) &&
		       (category.Length == name.Length || category[name.Length] == '.');
	}
}
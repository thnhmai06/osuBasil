using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

namespace Basil.Logging;

/// <summary>Appends each event that names a session (<c>Type</c> and <c>SessionId</c>) to that session's own file.</summary>
internal sealed class SessionLogSink(string directory, ITextFormatter formatter) : ILogEventSink
{
	public void Emit(LogEvent logEvent)
	{
		if (!logEvent.Properties.TryGetValue("SessionId", out var id) || id is not ScalarValue { Value: { } idValue } ||
		    !logEvent.Properties.TryGetValue("Type", out var type) || type is not ScalarValue { Value: { } typeValue})
			return;

		Directory.CreateDirectory(directory);
		// Lifecycle events are rare, so the file is opened for each instead of being held open per session.
		using var writer = new StreamWriter(Path.Combine(directory, $"{typeValue}-{idValue}.log"), true);
		formatter.Format(logEvent, writer);
	}
}

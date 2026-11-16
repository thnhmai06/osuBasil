using System.Globalization;

namespace Basil.Host.Cli.Dashboard.Data;

/// <summary>What the monitor calls and explains each measurement of the server, written for the operator who reads it.</summary>
/// <remarks>
///     Keys are the dotted names the sample is flattened to, for example <c>process.cpuPercent</c>; a host name, a
///     connection kind or a status class inside a key is written <c>*</c>. A measurement the table does not know still
///     shows, under a name made from its key, with the description of its category.
/// </remarks>
internal static class MonitorMetrics
{
	/// <summary>The key of the measurement of the exceptions of the last minute.</summary>
	public const string LastExceptions = "exceptions.lastMinute";

	private const string HostNote = "The server answers on several host names; the group is the host measured.";

	private static readonly IReadOnlyDictionary<string, string> Titles = new Dictionary<string, string>
	{
		["process"] = "Process",
		["gc"] = "Memory management",
		["threadpool"] = "Worker threads",
		["runtime"] = "Runtime",
		["exceptions"] = "Exceptions",
		["http"] = "HTTP",
		["application"] = "Application",
		["database"] = "Database",
		["stable"] = "osu! stable",
		["irc"] = "IRC",
		["events"] = "Event queue",
		["sessions"] = "Sessions",
		["connections"] = "Connections",
		["machine"] = "Machine"
	};

	private static readonly IReadOnlyDictionary<string, string> Groups = new Dictionary<string, string>
	{
		["api"] = "API",
		["irc"] = "IRC",
		["stable"] = "Stable",
		["stabletourney"] = "Tourney"
	};

	private static readonly IReadOnlyDictionary<string, string> CategoryNotes = new Dictionary<string, string>
	{
		["database"] = "A measurement of the database.",
		["events"] = "A measurement of the queue that carries the server's internal events to their handlers."
	};

	private static readonly IReadOnlyDictionary<string, MetricInfo> All = new Dictionary<string, MetricInfo>
	{
		["process.cpuPercent"] = new("CPU use", MetricUnit.Percent,
			"Share of all CPU cores the server process used over the last second, from 0 to 100. A high value for a long time means the server is busy or stuck in a loop."),
		["process.workingSetBytes"] = new("Memory in use", MetricUnit.Bytes,
			"Physical memory the server process holds now. A value that keeps growing without falling back points to a leak."),
		["process.privateMemoryBytes"] = new("Private memory", MetricUnit.Bytes,
			"Memory the server process reserved for itself, shared memory not counted. It is at least as large as what is really in use."),
		["process.handles"] = new("Open handles", MetricUnit.Count,
			"Number of files, sockets and other system handles the server holds open. A number that only grows means something is not closed."),
		["process.threads"] = new("Threads", MetricUnit.Count,
			"Number of operating-system threads of the server. A sudden rise means a lot of work is waiting on slow operations."),
		["gc.heapBytes"] = new("Managed heap", MetricUnit.Bytes,
			"Memory the garbage collector manages, as of its last run. It rises while the server works and falls after a collection."),
		["gc.generations"] = new("Generations", MetricUnit.Bytes,
			"Size of each part of the heap after the last collection: young objects first, then older ones, then large objects. A big last part means long-lived data.",
			true),
		["gc.collections"] = new("Collections", MetricUnit.Count,
			"How many times generation 0, 1 and 2 were collected since the server started. Many full collections (the last number) mean memory pressure.",
			true),
		["gc.pauseMilliseconds"] = new("GC pause total", MetricUnit.Milliseconds,
			"Total time the server stood still for garbage collection since it started. A fast-growing value makes the server feel slow."),
		["gc.allocatedBytesPerSecond"] = new("Allocation rate", MetricUnit.Bytes,
			"Memory the server allocated over the last second. A high rate makes collections more frequent."),
		["threadpool.threads"] = new("Pool threads", MetricUnit.Count,
			"Number of worker threads the server uses for its tasks. It grows when work blocks; a large number is a warning sign."),
		["threadpool.pendingWorkItems"] = new("Waiting tasks", MetricUnit.Count,
			"Tasks queued for a worker thread that has not picked them up yet. It should stay near 0; a growing value means the server cannot keep up."),
		["threadpool.completedPerSecond"] = new("Tasks per second", MetricUnit.Count,
			"How many tasks the workers finished over the last second. It follows how busy the server is."),
		["runtime.version"] = new("Runtime", MetricUnit.Text, "The .NET runtime the server runs on."),
		["runtime.os"] = new("OS", MetricUnit.Text, "The operating system the server runs on."),
		["runtime.build"] = new("Server build", MetricUnit.Text, "The version of the server program."),
		["runtime.uptimeSeconds"] = new("Uptime", MetricUnit.Seconds, "How long the server has been running."),
		["runtime.cpus"] = new("CPU cores", MetricUnit.Count, "Number of CPU cores the server may use."),
		["runtime.logsDropped"] = new("Log lines dropped", MetricUnit.Count,
			"Log lines thrown away because the log queue was full. It should be 0; anything else means logging cannot keep up and lines are missing.",
			Alarm: true),
		["runtime.environment"] = new("Environment", MetricUnit.Text,
			"The environment the server was started in, such as Production."),
		["exceptions.perSecond"] = new("Per second", MetricUnit.Count,
			"How many exceptions the server raised over the last second, handled ones included. Occasional ones are normal; a steady high rate means something keeps failing."),
		["exceptions.total"] = new("Total", MetricUnit.Count,
			"How many exceptions the server raised since it started or since the count was reset."),
		[LastExceptions] = new("Last exceptions (1m)", MetricUnit.Text,
			"The ten exception types raised most often during the last 60 seconds, each with how many times; equal types are merged. Learned from the growth of the total, so a burst of more than twenty exceptions in one second is undercounted."),
		["http.*.requestsPerSecond"] = new("Requests per second", MetricUnit.Count,
			$"How many HTTP requests this host answered over the last second. It shows how much traffic arrives. {HostNote}"),
		["http.*.p50Milliseconds"] = new("Response time p50", MetricUnit.Milliseconds,
			$"The median time this host took to answer a request: half of the requests were faster. Low is good. {HostNote}"),
		["http.*.p95Milliseconds"] = new("Response time p95", MetricUnit.Milliseconds,
			$"95 of 100 requests were answered at least this fast. A big gap to the median means some requests are slow. {HostNote}"),
		["http.*.p99Milliseconds"] = new("Response time p99", MetricUnit.Milliseconds,
			$"99 of 100 requests were answered at least this fast. It shows the slowest requests users feel. {HostNote}"),
		["http.*.statuses.*"] = new("Status {0}", MetricUnit.Count,
			$"How many requests got an answer of this class over the last second: 2xx succeeded, 3xx redirected, 4xx were refused or wrong, 5xx failed in the server. Many 5xx mean the server has a problem. {HostNote}"),
		["application.usersOnline"] = new("Users online", MetricUnit.Count,
			"Number of accounts that have at least one open connection."),
		["application.rooms"] = new("Open rooms", MetricUnit.Count, "Number of multiplayer rooms that exist now."),
		["application.roundsInProgress"] = new("Rounds playing", MetricUnit.Count,
			"Number of rooms whose round is running now."),
		["application.channels"] = new("Chat channels", MetricUnit.Count, "Number of general chat channels that exist now."),
		["application.countdowns"] = new("Countdowns", MetricUnit.Count,
			"Number of room countdowns that BasilBot is counting down now."),
		["application.openStreams"] = new("Live streams", MetricUnit.Count,
			"Number of live HTTP streams that clients keep open."),
		["application.diagnosticsWatchers"] = new("Diagnostics watchers", MetricUnit.Count,
			"Number of clients that follow this very feed now."),
		["database.commandsTotal"] = new("Commands run", MetricUnit.Count,
			"How many commands the server sent to the database since it started. It shows how much the database is used."),
		["database.commandsFailedTotal"] = new("Commands failed", MetricUnit.Count,
			"How many database commands failed since the server started. It should stay 0; anything else means data may not have been saved.",
			Alarm: true),
		["database.recentCommandP50Microseconds"] = new("Command time p50", MetricUnit.Microseconds,
			"The median time of the last 256 database commands: half were faster. Low is good."),
		["database.recentCommandP95Microseconds"] = new("Command time p95", MetricUnit.Microseconds,
			"95 of 100 of the last 256 database commands finished at least this fast. A big gap to the median means some queries are slow."),
		["database.connectionsInUse"] = new("Pool in use", MetricUnit.Count,
			"Database connections working now. Close to the pool limit means requests wait for a free connection. Not measured by every database engine."),
		["database.connectionsIdle"] = new("Pool idle", MetricUnit.Count,
			"Database connections that are open and free for the next command. Not measured by every database engine."),
		["stable.osu"] = new("osu! clients", MetricUnit.Count, "Number of osu! game clients connected now."),
		["stable.tourney"] = new("Tourney clients", MetricUnit.Count, "Number of osu!tourney clients connected now."),
		["stable.requestsPerSecond"] = new("Polls per second", MetricUnit.Count,
			"How many requests the osu! clients sent over the last second. It follows how many players are online."),
		["stable.p50Milliseconds"] = new("Poll time p50", MetricUnit.Milliseconds,
			"The median time the server took to answer an osu! client poll. Low is good."),
		["stable.p95Milliseconds"] = new("Poll time p95", MetricUnit.Milliseconds,
			"95 of 100 polls were answered at least this fast."),
		["stable.p99Milliseconds"] = new("Poll time p99", MetricUnit.Milliseconds,
			"99 of 100 polls were answered at least this fast. Players feel lag when it is high."),
		["irc.connections"] = new("IRC clients", MetricUnit.Count, "Number of IRC clients connected now."),
		["irc.requestsPerSecond"] = new("Lines per second", MetricUnit.Count,
			"How many IRC lines the server handled over the last second."),
		["irc.p50Milliseconds"] = new("Line time p50", MetricUnit.Milliseconds,
			"The median time the server took to handle an IRC line. Low is good."),
		["irc.p95Milliseconds"] = new("Line time p95", MetricUnit.Milliseconds,
			"95 of 100 IRC lines were handled at least this fast."),
		["irc.p99Milliseconds"] = new("Line time p99", MetricUnit.Milliseconds,
			"99 of 100 IRC lines were handled at least this fast."),
		["events.publishedTotal"] = new("Published", MetricUnit.Count,
			"How many internal events the server raised since it started. It shows how much happens."),
		["events.pending"] = new("Pending", MetricUnit.Count,
			"Events raised but not yet handled. It should stay near 0; a growing number means handlers are too slow."),
		["events.handlerFailuresTotal"] = new("Handler failures", MetricUnit.Count,
			"How many times an event handler failed since the server started. It should be 0; anything else means the effect of an event was lost.",
			Alarm: true),
		["sessions.stable"] = new("Stable", MetricUnit.Count, "Number of osu! game client sessions open now."),
		["sessions.stabletourney"] = new("Tourney", MetricUnit.Count, "Number of osu!tourney client sessions open now."),
		["sessions.irc"] = new("IRC", MetricUnit.Count, "Number of IRC sessions open now."),
		["sessions.api"] = new("API", MetricUnit.Count, "Number of HTTP API sessions open now."),
		["connections.*.opened"] = new("Opened", MetricUnit.Count,
			"How many connections of this kind were opened since the server started."),
		["connections.*.closed"] = new("Closed", MetricUnit.Count,
			"How many connections of this kind were closed since the server started, whatever the reason."),
		["connections.*.rejected"] = new("Rejected", MetricUnit.Count,
			"How many connections of this kind were refused because the user already held too many. Many means users hit the limit.",
			Alarm: true),
		["connections.*.timeout"] = new("Timed out", MetricUnit.Count,
			"How many connections of this kind were closed because the client sent nothing for too long.", Alarm: true),
		["connections.*.errors"] = new("Lost", MetricUnit.Count,
			"How many connections of this kind were closed because the transport was lost, such as a dropped network.",
			Alarm: true),
		["machine.totalMemoryBytes"] = new("Memory total", MetricUnit.Bytes, "Memory of the whole machine."),
		["machine.usedMemoryBytes"] = new("Memory used", MetricUnit.Bytes,
			"Memory in use on the whole machine, every program counted. Near the total means the machine may start to swap."),
		["machine.networkSentBytesPerSecond"] = new("Network sent", MetricUnit.Bytes,
			"Data the machine sent over the network over the last second, loopback excluded."),
		["machine.networkReceivedBytesPerSecond"] = new("Network received", MetricUnit.Bytes,
			"Data the machine received over the network over the last second, loopback excluded.")
	};

	/// <summary>Gets the title of a category.</summary>
	/// <param name="category">The first part of a dotted name.</param>
	public static string Title(string category)
	{
		return Titles.TryGetValue(category, out var title) ? title : Capital(category);
	}

	/// <summary>Gets the title of a group inside a category, such as a host or a kind of connection.</summary>
	/// <param name="group">The part of the key that names the group.</param>
	public static string GroupTitle(string group)
	{
		return Groups.TryGetValue(group, out var title) ? title : Capital(group);
	}

	/// <summary>Describes a measurement.</summary>
	/// <param name="key">The dotted name.</param>
	/// <returns>The name inside its group, the unit and the explanation.</returns>
	public static MetricInfo Of(string key)
	{
		var (generic, data) = Generic(key);
		if (All.TryGetValue(generic, out var known)) return known with { Label = known.Label.Replace("{0}", data) };

		var parts = key.Split('.');
		var category = parts[0];
		var name = string.Join(' ', parts.Skip(parts.Length > 2 ? 2 : 1));
		return new MetricInfo(Capital(Words(name)), MetricUnit.Text,
			CategoryNotes.GetValueOrDefault(category, $"A measurement of {Title(category).ToLowerInvariant()}.") +
			" This one has no longer explanation yet.");
	}

	/// <summary>Writes a value for the operator, in the unit of its measurement.</summary>
	/// <param name="info">The measurement the value belongs to.</param>
	/// <param name="value">The value as the sample holds it.</param>
	/// <param name="ascii">Whether the dashboard stays in ASCII.</param>
	public static string Show(MetricInfo info, string value, bool ascii)
	{
		if (info.List)
			return string.Join(info.Unit == MetricUnit.Bytes ? " " : " / ",
				value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(part => Show(info with { List = false }, part, ascii)));

		if (info.Unit == MetricUnit.Text || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
			return value;

		return info.Unit switch
		{
			MetricUnit.Bytes => Bytes(number),
			MetricUnit.Percent => $"{number:0.#} %",
			MetricUnit.Milliseconds => number < 10 ? $"{number:0.00} ms" : $"{number:0} ms",
			MetricUnit.Microseconds => number < 1000 ? $"{number:0} {(ascii ? "us" : "µs")}" : $"{number / 1000:0.0} ms",
			MetricUnit.Seconds => Duration(TimeSpan.FromSeconds(number)),
			_ => number == Math.Floor(number) ? number.ToString("N0", CultureInfo.InvariantCulture)
				: number.ToString("0.##", CultureInfo.InvariantCulture)
		};
	}

	private static string Bytes(double bytes)
	{
		string[] units = ["B", "KB", "MB", "GB", "TB"];
		var unit = 0;
		while (bytes >= 1024 && unit < units.Length - 1)
		{
			bytes /= 1024;
			unit++;
		}

		return $"{bytes.ToString(unit == 0 ? "0" : "0.0", CultureInfo.InvariantCulture)} {units[unit]}";
	}

	private static string Duration(TimeSpan span)
	{
		return span.TotalDays >= 1 ? $"{(int)span.TotalDays}d {span.Hours}h"
			: span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes:00}m"
			: span.TotalMinutes >= 1 ? $"{span.Minutes}m {span.Seconds:00}s"
			: $"{span.Seconds}s";
	}

	// The host of an HTTP measurement, the status class under it and the kind of a connection event are data, not names.
	private static (string Key, string Data) Generic(string key)
	{
		var parts = key.Split('.');
		var data = string.Empty;
		if (parts is ["http", _, ..]) parts[1] = "*";
		if (parts is ["http", "*", "statuses", _])
		{
			data = parts[3];
			parts[3] = "*";
		}

		if (parts is ["connections", _, _]) parts[1] = "*";
		return (string.Join('.', parts), data);
	}

	private static string Words(string camel)
	{
		return string.Concat(camel.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));
	}

	private static string Capital(string text)
	{
		return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
	}
}

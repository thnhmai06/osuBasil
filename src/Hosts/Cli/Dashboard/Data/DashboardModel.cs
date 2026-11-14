using Basil.Host.Cli.Commands;
using Microsoft.Extensions.Logging;

namespace Basil.Host.Cli.Dashboard.Data;

/// <summary>One account that is online, as the users tab lists it.</summary>
/// <param name="Id">The account id.</param>
/// <param name="Name">The account name.</param>
/// <param name="Connections">The kinds of connection it holds, such as <c>osu irc</c>.</param>
/// <param name="Status">What the osu! client reports it is doing.</param>
/// <param name="Room">The room it is in, or <c>-</c>.</param>
/// <param name="Idle">How long since the last activity.</param>
/// <param name="Privileges">The effective privileges.</param>
public sealed record UserRow(
	int Id,
	string Name,
	string Connections,
	string Status,
	string Room,
	TimeSpan Idle,
	ulong Privileges);

/// <summary>One slot of the room that is selected.</summary>
/// <param name="Index">The slot number.</param>
/// <param name="Player">The player, or <c>-</c>.</param>
/// <param name="State">The slot's status.</param>
public sealed record SlotRow(int Index, string Player, string State);

/// <summary>One open room, as the rooms tab lists it.</summary>
/// <param name="Id">The room id.</param>
/// <param name="Name">The room name.</param>
/// <param name="Players">How many players sit in it.</param>
/// <param name="Size">How many slots are usable.</param>
/// <param name="State">Playing, waiting or locked.</param>
/// <param name="Host">The host, or <c>-</c>.</param>
/// <param name="Slots">The slots.</param>
public sealed record RoomRow(
	int Id,
	string Name,
	int Players,
	int Size,
	string State,
	string Host,
	IReadOnlyList<SlotRow> Slots);

/// <summary>How long the server takes to handle one request, in milliseconds, at three percentiles.</summary>
/// <param name="P50">The median.</param>
/// <param name="P95">The 95th percentile.</param>
/// <param name="P99">The 99th percentile.</param>
public readonly record struct Latency(double P50, double P95, double P99);

/// <summary>The tabs of the dashboard, in the order of F1 to F10.</summary>
public enum DashboardTab : byte
{
	/// <summary>The log and the command box.</summary>
	Console,

	/// <summary>The graphs of the machine and the server.</summary>
	Monitor,

	/// <summary>The accounts.</summary>
	Users,

	/// <summary>The beatmaps.</summary>
	Beatmaps,

	/// <summary>The rooms.</summary>
	Lobby,

	/// <summary>The chat.</summary>
	Chat,

	/// <summary>The scores.</summary>
	Scores,

	/// <summary>The connections by kind.</summary>
	Connections,

	/// <summary>The server settings.</summary>
	Settings,

	/// <summary>The version, the licence and the updates.</summary>
	About
}

/// <summary>Everything the dashboard shows, refreshed once a second; the tabs only read it.</summary>
public sealed class DashboardModel
{
	/// <summary>How many seconds of history the graphs keep.</summary>
	public const int HistorySeconds = 15 * 60;

	/// <summary>The kinds of connection the server counts, as the diagnostics name them.</summary>
	public static readonly string[] ConnectionKinds = ["stable", "stabletourney", "irc", "api"];

	private readonly List<OutputLine> _output = [];
	private readonly Lock _outputGate = new();

	/// <summary>Gets the name of the server's version.</summary>
	public string Version { get; set; } = string.Empty;

	/// <summary>Gets the process CPU use, in percent of all cores.</summary>
	public Series Cpu { get; } = new(HistorySeconds);

	/// <summary>Gets the working set, in megabytes.</summary>
	public Series Memory { get; } = new(HistorySeconds);

	/// <summary>Gets the number of accounts online.</summary>
	public Series UsersOnline { get; } = new(HistorySeconds);

	/// <summary>Gets the number of open rooms.</summary>
	public Series Rooms { get; } = new(HistorySeconds);

	/// <summary>Gets the HTTP requests per second of every host together.</summary>
	public Series Requests { get; } = new(HistorySeconds);

	/// <summary>Gets the managed heap, in megabytes.</summary>
	public Series Heap { get; } = new(HistorySeconds);

	/// <summary>Gets the allocation rate, in megabytes per second.</summary>
	public Series Allocations { get; } = new(HistorySeconds);

	/// <summary>Gets the CPU use of the whole machine, in percent of all cores.</summary>
	public Series MachineCpu { get; } = new(HistorySeconds);

	/// <summary>Gets the memory used on the whole machine, in gigabytes.</summary>
	public Series MachineMemory { get; } = new(HistorySeconds);

	/// <summary>Gets the working set of the process, in gigabytes.</summary>
	public Series ProcessMemory { get; } = new(HistorySeconds);

	/// <summary>Gets the memory of the whole machine, in gigabytes.</summary>
	public double MachineMemoryTotal { get; set; }

	/// <summary>Gets the network traffic sent by the machine, in megabytes per second.</summary>
	public Series NetworkSent { get; } = new(HistorySeconds);

	/// <summary>Gets the network traffic received by the machine, in megabytes per second.</summary>
	public Series NetworkReceived { get; } = new(HistorySeconds);

	/// <summary>Gets the connections open, of every kind together.</summary>
	public Series ConnectionsOpen { get; } = new(HistorySeconds);

	/// <summary>Gets the connections open by kind (<c>stable</c>, <c>stabletourney</c>, <c>irc</c>, <c>api</c>).</summary>
	public IReadOnlyDictionary<string, Series> OpenByKind { get; } =
		ConnectionKinds.ToDictionary(kind => kind, _ => new Series(HistorySeconds));

	/// <summary>Gets the connection events counted since the server started, by <c>&lt;kind&gt;.&lt;event&gt;</c>.</summary>
	public IReadOnlyDictionary<string, long> ConnectionEvents { get; set; } = new Dictionary<string, long>();

	/// <summary>Gets how long a poll or an IRC line takes, by protocol (<c>stable</c>, <c>irc</c>, <c>api</c>).</summary>
	public IReadOnlyDictionary<string, Latency> Latencies { get; set; } = new Dictionary<string, Latency>();

	/// <summary>Gets the busy part of each core, from 0 to 100.</summary>
	public double[] Cores { get; set; } = [];

	/// <summary>Gets the connections online by kind.</summary>
	public IReadOnlyDictionary<string, int> Sessions { get; set; } = new Dictionary<string, int>();

	/// <summary>Gets the latest value of every diagnostics category, flattened to dotted names.</summary>
	public IReadOnlyList<(string Name, string Value)> Diagnostics { get; set; } = [];

	/// <summary>Gets the accounts online.</summary>
	public IReadOnlyList<UserRow> Users { get; set; } = [];

	/// <summary>Gets the open rooms.</summary>
	public IReadOnlyList<RoomRow> RoomList { get; set; } = [];

	/// <summary>Gets the messages of the chat tab.</summary>
	public ChatFeed Chat { get; init; } = new();

	/// <summary>Gets the log.</summary>
	public LogBuffer Log { get; init; } = null!;

	/// <summary>Gets the tab in view.</summary>
	public DashboardTab Tab { get; set; }

	/// <summary>Gets the index of the selected row of the users or rooms table.</summary>
	public int Selected { get; set; }

	/// <summary>Gets the text that filters the table or the log in view.</summary>
	public string Filter { get; set; } = string.Empty;

	/// <summary>Gets the lowest level the log tab shows.</summary>
	public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

	/// <summary>Gets a value that indicates whether the log stands still instead of following the newest line.</summary>
	public bool LogPaused { get; set; }

	/// <summary>Gets the lines shown in the log while it is paused.</summary>
	public IReadOnlyList<LogLine>? LogFrozen { get; set; }

	/// <summary>Gets the output of the commands typed in the command box.</summary>
	public IReadOnlyList<OutputLine> Output
	{
		get
		{
			lock (_outputGate)
			{
				return [.. _output];
			}
		}
	}

	/// <summary>Gets the message of the Ctrl+C state, or an empty string.</summary>
	public string Notice { get; set; } = string.Empty;

	/// <summary>Gets the last status of the shutdown, or an empty string.</summary>
	public string ShutdownStatus { get; set; } = string.Empty;

	/// <summary>Adds a line to the output of the command box, keeping the most recent 200.</summary>
	public void AddOutput(OutputLine line)
	{
		lock (_outputGate)
		{
			_output.Add(line);
			if (_output.Count > 200) _output.RemoveAt(0);
		}
	}
}
using Basil.Host.Cli.Commands;

namespace Basil.Host.Cli.Dashboard.Data;

/// <summary>One account that is online, as the users tab lists it.</summary>
/// <param name="Id">The account id.</param>
/// <param name="Name">The account name.</param>
/// <param name="Connections">The kinds of connection it holds, such as <c>osu irc</c>.</param>
/// <param name="Status">What the osu! client reports it is doing.</param>
/// <param name="Room">The room it is in, or <c>-</c>.</param>
/// <param name="Idle">How long since the last activity.</param>
/// <param name="Permissions">The effective permissions.</param>
public sealed record UserRow(int Id, string Name, string Connections, string Status, string Room, TimeSpan Idle, ulong Permissions);

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
public sealed record RoomRow(int Id, string Name, int Players, int Size, string State, string Host, IReadOnlyList<SlotRow> Slots);

/// <summary>The tabs of the dashboard.</summary>
public enum DashboardTab : byte
{
	/// <summary>The summary.</summary>
	Overview,

	/// <summary>The accounts online.</summary>
	Users,

	/// <summary>The open rooms.</summary>
	Rooms,

	/// <summary>Every diagnostics category with its history.</summary>
	Metrics,

	/// <summary>The server log.</summary>
	Logs,

	/// <summary>The chat feed.</summary>
	Chat
}

/// <summary>Everything the dashboard shows, refreshed once a second; the tabs only read it.</summary>
public sealed class DashboardModel
{
	/// <summary>How many seconds of history the graphs keep.</summary>
	public const int HistorySeconds = 15 * 60;

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
	public Microsoft.Extensions.Logging.LogLevel MinimumLevel { get; set; } = Microsoft.Extensions.Logging.LogLevel.Information;

	/// <summary>Gets a value that indicates whether the log stands still instead of following the newest line.</summary>
	public bool LogPaused { get; set; }

	/// <summary>Gets the lines shown in the log while it is paused.</summary>
	public IReadOnlyList<LogLine>? LogFrozen { get; set; }

	private readonly List<OutputLine> _output = [];
	private readonly Lock _outputGate = new();

	/// <summary>Gets the output of the commands typed in the command box.</summary>
	public IReadOnlyList<OutputLine> Output
	{
		get
		{
			lock (_outputGate) return [.. _output];
		}
	}

	/// <summary>Adds a line to the output of the command box, keeping the most recent 200.</summary>
	public void AddOutput(OutputLine line)
	{
		lock (_outputGate)
		{
			_output.Add(line);
			if (_output.Count > 200) _output.RemoveAt(0);
		}
	}

	/// <summary>Gets the message of the Ctrl+C state, or an empty string.</summary>
	public string Notice { get; set; } = string.Empty;

	/// <summary>Gets the last status of the shutdown, or an empty string.</summary>
	public string ShutdownStatus { get; set; } = string.Empty;
}

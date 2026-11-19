using Basil.Host.Cli.Commands;
using Microsoft.Extensions.Logging;

namespace Basil.Host.Cli.Dashboard.Data;

public sealed record UserRow(
	int Id,
	string Name,
	string Connections,
	string Status,
	string Room,
	TimeSpan Idle,
	ulong Privileges);

public sealed record SlotRow(int Index, string Player, string State);

public sealed record RoomRow(
	int Id,
	string Name,
	int Players,
	int Size,
	string State,
	string Host,
	IReadOnlyList<SlotRow> Slots);

public readonly record struct Latency(double P50, double P95, double P99);

public enum DashboardTab : byte
{
	Console,

	Monitor,

	Users,

	Beatmaps,

	Lobby,

	Chat,

	Scores,

	Sessions,

	Settings,

	About
}

public sealed class DashboardModel
{
	public const int HistorySeconds = 15 * 60;

	public static readonly string[] ConnectionKinds = ["stable", "stabletourney", "irc", "api"];

	private readonly List<OutputLine> _output = [];
	private readonly Lock _outputGate = new();

	public string Version { get; set; } = string.Empty;

	public Series Cpu { get; } = new(HistorySeconds);

	public Series Memory { get; } = new(HistorySeconds);

	public Series UsersOnline { get; } = new(HistorySeconds);

	public Series Rooms { get; } = new(HistorySeconds);

	public Series Requests { get; } = new(HistorySeconds);

	public Series Allocations { get; } = new(HistorySeconds);

	public Series ConnectionsOpen { get; } = new(HistorySeconds);

	public IReadOnlyDictionary<string, Series> OpenByKind { get; } =
		ConnectionKinds.ToDictionary(kind => kind, _ => new Series(HistorySeconds));

	public IReadOnlyDictionary<string, long> ConnectionEvents { get; set; } = new Dictionary<string, long>();

	public IReadOnlyDictionary<string, Latency> Latencies { get; set; } = new Dictionary<string, Latency>();

	public IReadOnlyDictionary<string, int> Sessions { get; set; } = new Dictionary<string, int>();

	public IReadOnlyList<(string Name, string Value)> Diagnostics { get; set; } = [];

	public IReadOnlyList<ExceptionCount> ExceptionsLastMinute { get; set; } = [];

	public IReadOnlyList<UserRow> Users { get; set; } = [];

	public IReadOnlyList<RoomRow> RoomList { get; set; } = [];

	public ChatFeed Chat { get; init; } = new();

	public LogBuffer Log { get; init; } = null!;

	public DashboardTab Tab { get; set; }

	public int Selected { get; set; }

	public string Filter { get; set; } = string.Empty;

	public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

	public bool LogPaused { get; set; }

	public IReadOnlyList<LogLine>? LogFrozen { get; set; }

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

	public string Notice { get; set; } = string.Empty;

	public string ShutdownStatus { get; set; } = string.Empty;

	public void AddOutput(OutputLine line)
	{
		lock (_outputGate)
		{
			_output.Add(line);
			if (_output.Count > 200) _output.RemoveAt(0);
		}
	}
}
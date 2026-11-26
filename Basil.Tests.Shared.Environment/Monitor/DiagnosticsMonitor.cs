using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Basil.Tests.Shared.Clients;
using Basil.Tests.Shared.Clients.Rest;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Environment.Monitor;

/// <summary>
///     Summary of server-side metrics collected from Basil's diagnostics SSE during a run; baseline is the first
///     sample (only the monitor signed in), final the latest.
/// </summary>
public sealed record ServerMetricsSummary(
	int Samples,
	double AvgCpuPercent,
	double MaxCpuPercent,
	double MaxWorkingSetMb,
	double MaxGcHeapMb,
	long MaxThreadPoolPending,
	double MaxExceptionsPerSec,
	long PeakUsersOnline,
	long PeakOpenRooms,
	long PeakRoundsInProgress,
	long PeakLiveStreams,
	double MaxStableP95Ms,
	double MaxIrcP95Ms,
	double MaxDbP95Us,
	long MaxEventsPending,
	long TotalEventFailures,
	bool RestartDetected,
	long BaselineUsersOnline,
	long FinalUsersOnline,
	long FinalOpenRooms);

/// <summary>
///     Streams Basil's <c>/v1/diagnostics</c> SSE feed into NBomber custom gauges and writes <c>diagnostics.jsonl</c>
///     and <c>server-summary.json</c> beside the report.
/// </summary>
public sealed class DiagnosticsMonitor(
	Endpoint endpoint,
	HttpClient http,
	string developerUser,
	string developerPassword,
	string reportDirectory) : IAsyncDisposable
{
	public const string CpuGaugeName = "server_cpu_percent";
	public const string WorkingSetGaugeName = "server_working_set_mb";
	public const string GcHeapGaugeName = "server_gc_heap_mb";
	public const string ThreadPoolPendingGaugeName = "server_threadpool_pending";
	public const string ExceptionsRateGaugeName = "server_exceptions_per_sec";
	public const string UsersOnlineGaugeName = "server_users_online";
	public const string OpenRoomsGaugeName = "server_open_rooms";
	public const string RoundsInProgressGaugeName = "server_rounds_in_progress";
	public const string LiveStreamsGaugeName = "server_live_streams";
	public const string StableP95GaugeName = "server_stable_p95_ms";
	public const string IrcP95GaugeName = "server_irc_p95_ms";
	public const string DbP95GaugeName = "server_db_p95_us";
	public const string EventsPendingGaugeName = "server_events_pending";
	public const string EventsFailuresGaugeName = "server_events_failures";
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

	private readonly List<(DateTimeOffset At, double Percent)> _cpu = [];
	private readonly ConcurrentDictionary<string, Action<double>> _gauges = new();
	private readonly CancellationTokenSource _stop = new();

	private readonly Lock _sync = new();
	private long _baselineUsers = -1;
	private long _finalRooms;
	private long _finalUsers;
	private long _lastUptimeSeconds = -1;
	private Task _loop = Task.CompletedTask;
	private double _maxCpu;
	private double _maxDbP95;
	private long _maxEventsPending;
	private double _maxExceptionsPerSec;
	private double _maxGcHeapMb;
	private double _maxIrcP95;
	private double _maxStableP95;
	private long _maxTpPending;
	private double _maxWorkingSetMb;
	private long _peakRooms;
	private long _peakRounds;
	private long _peakStreams;
	private long _peakUsers;
	private bool _restartDetected;

	private int _samples;
	private int _started;
	private double _sumCpu;
	private long _totalEventFailures;

	/// <summary>
	///     Gets the signed-in client of the monitor, or <see langword="null" /> while it has none; the developer account
	///     may hold only one API session, so other readers of it share this one.
	/// </summary>
	public RestClient? Client { get; private set; }

	/// <summary>Stops streaming and writes <c>server-summary.json</c>.</summary>
	public async ValueTask DisposeAsync()
	{
		await _stop.CancelAsync();
		try
		{
			await _loop;
		}
		catch (Exception)
		{
			// Shutting down.
		}

		Directory.CreateDirectory(reportDirectory);
		var summaryPath = Path.Combine(reportDirectory, "server-summary.json");
		await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(Summary(), Json), new UTF8Encoding(false));
		_stop.Dispose();
	}

	/// <summary>Registers every server gauge on an NBomber scenario init context.</summary>
	public void RegisterMetrics(IScenarioInitContext context)
	{
		Register(CpuGaugeName, "%");
		Register(WorkingSetGaugeName, "MB");
		Register(GcHeapGaugeName, "MB");
		Register(ThreadPoolPendingGaugeName, "items");
		Register(ExceptionsRateGaugeName, "/s");
		Register(UsersOnlineGaugeName, "users");
		Register(OpenRoomsGaugeName, "rooms");
		Register(RoundsInProgressGaugeName, "rounds");
		Register(LiveStreamsGaugeName, "streams");
		Register(StableP95GaugeName, "ms");
		Register(IrcP95GaugeName, "ms");
		Register(DbP95GaugeName, "us");
		Register(EventsPendingGaugeName, "events");
		Register(EventsFailuresGaugeName, "count");
		return;

		void Register(string name, string unit)
		{
			var gauge = Metric.CreateGauge(name, unit);
			gauge.Set(0);
			context.RegisterMetric(gauge);
			_gauges[name] = value => gauge.Set(value);
		}
	}

	/// <summary>Starts streaming diagnostics in the background (idempotent).</summary>
	public void Start()
	{
		if (Interlocked.Exchange(ref _started, 1) != 0) return;
		Directory.CreateDirectory(reportDirectory);
		_loop = Task.Run(() => RunAsync(_stop.Token));
	}

	/// <summary>Gets the summary collected so far.</summary>
	public ServerMetricsSummary Summary()
	{
		lock (_sync)
		{
			return new ServerMetricsSummary(
				_samples,
				_samples == 0 ? 0 : Math.Round(_sumCpu / _samples, 2),
				Math.Round(_maxCpu, 2),
				Math.Round(_maxWorkingSetMb, 2),
				Math.Round(_maxGcHeapMb, 2),
				_maxTpPending,
				Math.Round(_maxExceptionsPerSec, 2),
				_peakUsers,
				_peakRooms,
				_peakRounds,
				_peakStreams,
				Math.Round(_maxStableP95, 2),
				Math.Round(_maxIrcP95, 2),
				Math.Round(_maxDbP95, 2),
				_maxEventsPending,
				_totalEventFailures,
				_restartDetected,
				Math.Max(0, _baselineUsers),
				_finalUsers,
				_finalRooms);
		}
	}

	/// <summary>
	///     Gets the 95th percentile of the server CPU percent sampled inside a time window, or <see langword="null" />
	///     when no sample fell inside it.
	/// </summary>
	public double? CpuP95Between(DateTimeOffset from, DateTimeOffset to)
	{
		lock (_sync)
		{
			var inside = _cpu.Where(sample => sample.At >= from && sample.At <= to).Select(sample => sample.Percent)
				.Order().ToArray();
			// Nearest rank: the smallest sample that at least 95 % of the samples do not exceed.
			return inside.Length == 0 ? null : Math.Round(inside[(int)Math.Ceiling(inside.Length * 0.95) - 1], 2);
		}
	}

	private async Task RunAsync(CancellationToken ct)
	{
		var jsonlPath = Path.Combine(reportDirectory, "diagnostics.jsonl");
		await using var writer = new StreamWriter(jsonlPath, true, new UTF8Encoding(false));
		writer.AutoFlush = true;

		while (!ct.IsCancellationRequested)
		{
			var rest = new RestClient(endpoint, http);
			try
			{
				var login = await rest.SignInAsync(developerUser, developerPassword, ct);
				if (!login.Success)
				{
					await Task.Delay(TimeSpan.FromSeconds(5), ct);
					continue;
				}

				Client = rest;
				using var stream = await rest.OpenStreamAsync("/v1/diagnostics", true, ct);
				await foreach (var ev in stream.ReadAsync(ct))
				{
					await writer.WriteLineAsync(JsonSerializer.Serialize(new
					{
						at = DateTimeOffset.UtcNow,
						ev.Event,
						data = ev.Data
					}));
					IngestEvent(ev.Data);
				}
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				break;
			}
			catch (Exception)
			{
				// Reconnect after a short pause if the stream drops or the server restarts.
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(2), ct);
				}
				catch (OperationCanceledException)
				{
					break;
				}
			}
			finally
			{
				Client = null;
				if (rest.Token is not null)
					try
					{
						using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
						await rest.SignOutAsync(timeout.Token);
					}
					catch (Exception)
					{
						// Best effort.
					}
			}
		}
	}

	private void IngestEvent(string json)
	{
		try
		{
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			if (root.ValueKind == JsonValueKind.Array)
			{
				foreach (var item in root.EnumerateArray())
					if (item.ValueKind == JsonValueKind.Object)
						IngestSample(item);
			}
			else if (root.ValueKind == JsonValueKind.Object)
			{
				IngestSample(root);
			}
		}
		catch (JsonException)
		{
			// Ignore malformed events.
		}
	}

	private void IngestSample(JsonElement sample)
	{
		const double bytesPerMb = 1024.0 * 1024.0;
		var cpu = ReadDouble(sample, "process", "cpuPercent");
		var wsMb = ReadDouble(sample, "process", "workingSetBytes") / bytesPerMb;
		var gcMb = ReadDouble(sample, "gc", "heapBytes") / bytesPerMb;
		var tpPending = ReadLong(sample, "threadpool", "pendingWorkItems");
		var excRate = ReadDouble(sample, "exceptions", "perSecond");
		var uptime = ReadLong(sample, "runtime", "uptimeSeconds", -1);
		var users = ReadLong(sample, "application", "usersOnline");
		var rooms = ReadLong(sample, "application", "rooms");
		var rounds = ReadLong(sample, "application", "roundsInProgress");
		var streams = ReadLong(sample, "application", "liveStreams");
		var stableP95 = ReadDouble(sample, "stable", "p95Milliseconds");
		var ircP95 = ReadDouble(sample, "irc", "p95Milliseconds");
		var dbP95 = ReadDouble(sample, "database", "recentCommandP95Microseconds");
		var evPending = ReadLong(sample, "events", "pending");
		var evFailures = ReadLong(sample, "events", "handlerFailuresTotal");

		SetGauge(CpuGaugeName, cpu);
		SetGauge(WorkingSetGaugeName, wsMb);
		SetGauge(GcHeapGaugeName, gcMb);
		SetGauge(ThreadPoolPendingGaugeName, tpPending);
		SetGauge(ExceptionsRateGaugeName, excRate);
		SetGauge(UsersOnlineGaugeName, users);
		SetGauge(OpenRoomsGaugeName, rooms);
		SetGauge(RoundsInProgressGaugeName, rounds);
		SetGauge(LiveStreamsGaugeName, streams);
		SetGauge(StableP95GaugeName, stableP95);
		SetGauge(IrcP95GaugeName, ircP95);
		SetGauge(DbP95GaugeName, dbP95);
		SetGauge(EventsPendingGaugeName, evPending);
		SetGauge(EventsFailuresGaugeName, evFailures);

		lock (_sync)
		{
			_samples++;
			_cpu.Add((DateTimeOffset.UtcNow, cpu));
			_sumCpu += cpu;
			_maxCpu = Math.Max(_maxCpu, cpu);
			_maxWorkingSetMb = Math.Max(_maxWorkingSetMb, wsMb);
			_maxGcHeapMb = Math.Max(_maxGcHeapMb, gcMb);
			_maxTpPending = Math.Max(_maxTpPending, tpPending);
			_maxExceptionsPerSec = Math.Max(_maxExceptionsPerSec, excRate);
			_peakUsers = Math.Max(_peakUsers, users);
			_peakRooms = Math.Max(_peakRooms, rooms);
			_peakRounds = Math.Max(_peakRounds, rounds);
			_peakStreams = Math.Max(_peakStreams, streams);
			_maxStableP95 = Math.Max(_maxStableP95, stableP95);
			_maxIrcP95 = Math.Max(_maxIrcP95, ircP95);
			_maxDbP95 = Math.Max(_maxDbP95, dbP95);
			_maxEventsPending = Math.Max(_maxEventsPending, evPending);
			_totalEventFailures = Math.Max(_totalEventFailures, evFailures);
			if (_baselineUsers < 0) _baselineUsers = users;
			_finalUsers = users;
			_finalRooms = rooms;
			if (uptime >= 0 && _lastUptimeSeconds >= 0 && uptime < _lastUptimeSeconds)
				_restartDetected = true;
			if (uptime >= 0) _lastUptimeSeconds = uptime;
		}
	}

	private void SetGauge(string name, double value)
	{
		if (_gauges.TryGetValue(name, out var set)) set(value);
	}

	private static double ReadDouble(JsonElement root, string section, string prop)
	{
		return root.TryGetProperty(section, out var s) && s.ValueKind == JsonValueKind.Object &&
		       s.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.Number
			? p.GetDouble()
			: 0;
	}

	private static long ReadLong(JsonElement root, string section, string prop, long fallback = 0)
	{
		return root.TryGetProperty(section, out var s) && s.ValueKind == JsonValueKind.Object &&
		       s.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.Number
			? p.GetInt64()
			: fallback;
	}
}
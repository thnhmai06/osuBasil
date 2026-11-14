using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Basil.Application.Diagnostics.Services;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Basil.Host.Shared.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Cli.Dashboard.Data;

/// <summary>Reads the server once a second into the dashboard's model, from the same sample the HTTP diagnostics stream.</summary>
public sealed class DashboardSampler(IServiceProvider services, DashboardModel model, TimeProvider clock)
{
	private readonly CoreUsage _cores = new();

	/// <summary>Takes one sample and updates the model.</summary>
	public void Sample()
	{
		var registry = services.GetRequiredService<UserRegistry>();
		var lobby = services.GetRequiredService<Lobby>();
		// The intake answers without waiting for anything, so the frame loop can take the sample where it stands.
		var sample = services.GetRequiredService<IDiagnosticsIntake>().TakeSampleAsync().AsTask().GetAwaiter()
			.GetResult();
		var json = JsonSerializer.SerializeToNode(sample.All, ApiJson.Options)!.AsObject();

		model.Version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
		model.Cpu.Add(Number(json, "process", "cpuPercent"));
		model.Memory.Add(Number(json, "process", "workingSetBytes") / 1_048_576);
		model.Heap.Add(Number(json, "gc", "heapBytes") / 1_048_576);
		model.Allocations.Add(Number(json, "gc", "allocatedBytesPerSecond") / 1_048_576);
		model.UsersOnline.Add(Number(json, "application", "usersOnline"));
		model.Rooms.Add(Number(json, "application", "rooms"));
		model.Requests.Add(RequestsPerSecond(json));
		model.Cores = _cores.Read();
		model.MachineCpu.Add(model.Cores.Length == 0 ? 0 : model.Cores.Average());
		const double gigabyte = 1_073_741_824;
		model.MachineMemory.Add(Number(json, "machine", "usedMemoryBytes") / gigabyte);
		model.ProcessMemory.Add(Number(json, "process", "workingSetBytes") / gigabyte);
		model.MachineMemoryTotal = Number(json, "machine", "totalMemoryBytes") / gigabyte;
		model.NetworkSent.Add(Number(json, "machine", "networkSentBytesPerSecond") / 1_048_576);
		model.NetworkReceived.Add(Number(json, "machine", "networkReceivedBytesPerSecond") / 1_048_576);
		model.Sessions = SessionCounts(json);
		model.ConnectionsOpen.Add(model.Sessions.Values.Sum());
		foreach (var kind in DashboardModel.ConnectionKinds)
			model.OpenByKind[kind].Add(model.Sessions.GetValueOrDefault(kind));

		model.ConnectionEvents = ConnectionEvents(json);
		model.Latencies = Latencies(json);

		var lines = new List<(string, string)>();
		foreach (var (category, value) in json) Flatten(lines, category, value);
		model.Diagnostics = lines;

		model.Users = UserRows(registry, lobby, clock.GetUtcNow());
		model.RoomList = RoomRows(lobby);
	}

	private static double RequestsPerSecond(JsonObject json)
	{
		return json["http"] is JsonObject hosts
			? hosts.Sum(host =>
				host.Value is JsonObject window && window["requestsPerSecond"] is { } value ? (double)value : 0)
			: 0;
	}

	private static Dictionary<string, long> ConnectionEvents(JsonObject json)
	{
		return json["connections"] is JsonObject counts
			? counts.ToDictionary(pair => pair.Key, pair => (long)(pair.Value ?? 0))
			: new Dictionary<string, long>();
	}

	private static Dictionary<string, Latency> Latencies(JsonObject json)
	{
		var result = new Dictionary<string, Latency>();
		foreach (var protocol in new[] { "stable", "irc" })
			if (json[protocol] is JsonObject)
				result[protocol] = new Latency(Number(json, protocol, "p50Milliseconds"),
					Number(json, protocol, "p95Milliseconds"), Number(json, protocol, "p99Milliseconds"));

		// The hosts of the API are reported one by one; the slowest of them stands for the API.
		if (json["http"] is JsonObject { Count: > 0 } hosts)
		{
			var windows = hosts.Select(host => host.Value).OfType<JsonObject>().ToList();
			double Worst(string name)
			{
				return windows.Count == 0 ? 0 : windows.Max(window => window[name] is JsonValue v && v.TryGetValue<double>(out var n) ? n : 0);
			}

			result["api"] = new Latency(Worst("p50Milliseconds"), Worst("p95Milliseconds"), Worst("p99Milliseconds"));
		}

		return result;
	}

	private static Dictionary<string, int> SessionCounts(JsonObject json)
	{
		return json["sessions"] is JsonObject sessions
			? sessions.ToDictionary(pair => pair.Key, pair => (int)(pair.Value ?? 0))
			: new Dictionary<string, int>();
	}

	private static List<UserRow> UserRows(UserRegistry registry, Lobby lobby, DateTimeOffset now)
	{
		return
		[
			.. registry.Online.Values.Where(user => !user.Record.IsBasilBot()).OrderBy(user => user.Record.Id)
				.Select(user =>
					new UserRow(
						user.Record.Id, user.Record.Value.Name,
						string.Join(' ', user.Connections.Select(c => c.Type.ToString().ToLowerInvariant()).Distinct()),
						user.Stable?.Status.Activity.ToString() ?? "-", RoomOf(lobby, user),
						now - user.Connections.Select(c => c.LastActiveAt).DefaultIfEmpty(now).Max(),
						(ulong)user.EffectivePrivileges(now)))
		];
	}

	private static List<RoomRow> RoomRows(Lobby lobby)
	{
		return
		[
			.. lobby.Rooms.Values.OrderBy(room => room.Id).Select(room => new RoomRow(room.Id, room.Settings.Name,
				room.Slots.Count(slot => slot.Player is not null), room.Slots.Count(slot => !slot.Locked),
				room.Rounds.InProgress ? "playing" : room.Slots.Locked ? "locked" : "waiting",
				room.Authority.Host?.Record.Value.Name ?? "-",
				[
					.. room.Slots.Select(slot => new SlotRow(slot.Index, slot.Player?.Record.Value.Name ?? "-",
						slot.Locked ? "locked" : slot.Status?.ToString() ?? "open"))
				]))
		];
	}

	private static string RoomOf(Lobby lobby, User user)
	{
		var room = lobby.Rooms.Values.FirstOrDefault(r =>
			r.Slots.Any(slot => slot.Player is not null && ReferenceEquals(slot.Player.User, user)));
		return room is null ? "-" : $"#{room.Id}";
	}

	private static double Number(JsonObject json, string category, string name)
	{
		return json[category] is JsonObject section && section[name] is JsonValue value &&
		       value.TryGetValue<double>(out var number)
			? number
			: 0;
	}

	private static void Flatten(List<(string, string)> lines, string prefix, JsonNode? node)
	{
		switch (node)
		{
			case JsonObject obj:
				foreach (var (key, value) in obj) Flatten(lines, $"{prefix}.{key}", value);
				break;
			case JsonArray array:
				lines.Add((prefix, string.Join(",", array.Select(item => item?.ToJsonString().Trim('"')))));
				break;
			default:
				lines.Add((prefix, node is JsonValue scalar && scalar.TryGetValue<double>(out var number)
					? Math.Round(number, 2).ToString(CultureInfo.InvariantCulture)
					: node?.ToJsonString().Trim('"') ?? "-"));
				break;
		}
	}
}

/// <summary>
///     Reads the busy part of each core of the machine; only Linux publishes it (<c>/proc/stat</c>), elsewhere there
///     is no list.
/// </summary>
public sealed class CoreUsage
{
	private (long Busy, long Total)[] _last = [];

	/// <summary>Reads the busy part of each core since the last reading, from 0 to 100.</summary>
	public double[] Read()
	{
		if (!OperatingSystem.IsLinux() || !File.Exists("/proc/stat")) return [];

		try
		{
			var current = File.ReadLines("/proc/stat").Where(line =>
					line.StartsWith("cpu", StringComparison.Ordinal) && line.Length > 3 && char.IsDigit(line[3]))
				.Select(line =>
				{
					var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(long.Parse)
						.ToArray();
					var total = fields.Sum();
					var idle = fields[3] + (fields.Length > 4 ? fields[4] : 0);
					return (Busy: total - idle, Total: total);
				}).ToArray();
			var result = current.Select((core, index) =>
			{
				if (index >= _last.Length) return 0d;

				var total = core.Total - _last[index].Total;
				return total <= 0 ? 0d : 100d * (core.Busy - _last[index].Busy) / total;
			}).ToArray();
			_last = current;
			return result;
		}
		catch (Exception exception) when (exception is IOException or FormatException or UnauthorizedAccessException)
		{
			return [];
		}
	}
}
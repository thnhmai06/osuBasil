using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Basil.Application.Multiplayer.Capabilities;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Auth;
using Basil.Host.Shared;
using Basil.Host.Shared.Diagnostics;
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
		var runtime = services.GetRequiredService<RuntimeSampler>();
		var sample = DiagnosticsSample.Create(runtime, registry, lobby, 0);
		var json = JsonSerializer.SerializeToNode(sample, ApiJson.Options)!.AsObject();

		model.Version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
		model.Cpu.Add(Number(json, "process", "cpuPercent"));
		model.Memory.Add(Number(json, "process", "workingSetBytes") / 1_048_576);
		model.Heap.Add(Number(json, "gc", "heapBytes") / 1_048_576);
		model.Allocations.Add(Number(json, "gc", "allocatedBytesPerSecond") / 1_048_576);
		model.UsersOnline.Add(Number(json, "application", "usersOnline"));
		model.Rooms.Add(Number(json, "application", "rooms"));
		model.Requests.Add(RequestsPerSecond(json));
		model.Cores = _cores.Read();
		model.Sessions = SessionCounts(json);

		var lines = new List<(string, string)>();
		foreach (var (category, value) in json) Flatten(lines, category, value);
		model.Diagnostics = lines;

		model.Users = UserRows(registry, lobby, clock.GetUtcNow());
		model.RoomList = RoomRows(lobby);
	}

	private static double RequestsPerSecond(JsonObject json)
	{
		return json["http"] is JsonObject hosts
			? hosts.Sum(host => host.Value is JsonObject window && window["requestsPerSecond"] is { } value ? (double)value : 0)
			: 0;
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
			.. registry.Online.Values.Where(user => user.Bot is null).OrderBy(user => user.Record.Id).Select(user => new UserRow(
				user.Record.Id, user.Record.Value.Name,
				string.Join(' ', user.Connections.Select(c => c.Type.ToString().ToLowerInvariant()).Distinct()),
				user.Bancho?.Status.Activity.ToString() ?? "-", RoomOf(lobby, user),
				now - user.Connections.Select(c => c.LastActiveAt).DefaultIfEmpty(now).Max(),
				(ulong)user.EffectivePermissions(now)))
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
				[.. room.Slots.Select(slot => new SlotRow(slot.Index, slot.Player?.Record.Value.Name ?? "-",
					slot.Locked ? "locked" : slot.Status?.ToString() ?? "open"))]))
		];
	}

	private static string RoomOf(Lobby lobby, User user)
	{
		var room = lobby.Rooms.Values.FirstOrDefault(r => r.Slots.Any(slot => slot.Player is not null && ReferenceEquals(slot.Player.User, user)));
		return room is null ? "-" : $"#{room.Id}";
	}

	private static double Number(JsonObject json, string category, string name)
	{
		return json[category] is JsonObject section && section[name] is JsonValue value && value.TryGetValue<double>(out var number)
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
					? Math.Round(number, 2).ToString(System.Globalization.CultureInfo.InvariantCulture)
					: node?.ToJsonString().Trim('"') ?? "-"));
				break;
		}
	}
}

/// <summary>Reads the busy part of each core of the machine; only Linux publishes it (<c>/proc/stat</c>), elsewhere there is no list.</summary>
public sealed class CoreUsage
{
	private (long Busy, long Total)[] _last = [];

	/// <summary>Reads the busy part of each core since the last reading, from 0 to 100.</summary>
	public double[] Read()
	{
		if (!OperatingSystem.IsLinux() || !File.Exists("/proc/stat")) return [];

		try
		{
			var current = File.ReadLines("/proc/stat").Where(line => line.StartsWith("cpu", StringComparison.Ordinal) && line.Length > 3 && char.IsDigit(line[3]))
				.Select(line =>
				{
					var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(long.Parse).ToArray();
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

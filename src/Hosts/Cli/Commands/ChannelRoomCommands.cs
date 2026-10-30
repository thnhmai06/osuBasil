using Basil.Application.Chat.Runtime;
using Basil.Application.Common.Common;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Multiplayer.Runtime;
using Basil.Application.Servers.Runtime;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Host.Shared.Views;

namespace Basil.Host.Cli.Commands;

/// <summary>The <c>channels</c>, <c>rooms</c> and <c>matches</c> commands.</summary>
internal static class ChannelRoomCommands
{
	private static readonly OptionDef[] ChannelFields =
	[
		new("topic", "The topic."), new("readPrivileges", "Who may read, as a bitmask.", OptionKind.Mask),
		new("writePrivileges", "Who may write, as a bitmask.", OptionKind.Mask),
		new("autoJoin", "Whether clients join it at login.", OptionKind.Bool),
		new("visible", "Whether it is listed.", OptionKind.Bool)
	];

	public static IEnumerable<CommandDef> Create()
	{
		yield return new CommandDef
		{
			Pattern = "channels list", Route = "GET /v1/channels", Offline = true, Summary = "Lists the general channels.",
			Run = ListChannelsAsync
		};
		yield return new CommandDef
		{
			Pattern = "channels create", Route = "POST /v1/channels", Offline = true, Summary = "Creates a general channel.",
			Options = [new("name", "The name, starting with #.", Required: true), .. ChannelFields], Run = CreateChannelAsync
		};
		yield return new CommandDef
		{
			Pattern = "channels <name> set", Route = "PATCH /v1/channels/{name}", Offline = true,
			Summary = "Changes the given fields of a general channel.", Options = ChannelFields, Run = SetChannelAsync
		};
		yield return new CommandDef
		{
			Pattern = "channels <name> delete", Route = "DELETE /v1/channels/{name}", Offline = true,
			Summary = "Deletes a general channel.", Run = DeleteChannelAsync
		};

		yield return new CommandDef
		{
			Pattern = "rooms list", Route = "GET /v1/rooms", Summary = "Lists the open rooms.", Options = CommandHelpers.Paging,
			Run = ListRoomsAsync
		};
		yield return new CommandDef
		{
			Pattern = "rooms create", Route = "POST /v1/rooms", Summary = "Opens a tournament room.",
			Options =
			[
				new("name", "The name of the room.", Required: true), new("password", "The password.", Secret: true),
				new("private", "Make the room private.", OptionKind.Bool)
			],
			Run = CreateRoomAsync
		};
		yield return new CommandDef
		{
			Pattern = "rooms <id:int>", Route = "GET /v1/rooms/{id}", Summary = "Shows a room with its slots.", Run = ShowRoomAsync
		};
		foreach (var command in RoomCommands.All)
		{
			var room = command;
			yield return new CommandDef
			{
				Pattern = $"rooms <id:int> {room.Name}", Route = $"POST /v1/rooms/{{id}}/{room.Name}", Summary = room.Summary,
				Options = OptionsOf(room.Body), Run = call => RunRoomAsync(call, room)
			};
		}

		yield return new CommandDef
		{
			Pattern = "matches list", Route = "GET /v1/matches", Offline = true, Summary = "Lists matches.",
			Options = [new("query", "Filters with ended: and private:."), .. CommandHelpers.Paging], Run = ListMatchesAsync
		};
		yield return new CommandDef
		{
			Pattern = "matches <id:int>", Route = "GET /v1/matches/{id}", Offline = true,
			Summary = "Shows a match with its report as it is now.", Run = ShowMatchAsync
		};
	}

	/// <summary>Turns the fields of a request body into options, the way the API names them.</summary>
	public static OptionDef[] OptionsOf(Type? body)
	{
		if (body is null) return [];

		return [.. body.GetConstructors()[0].GetParameters().Select(parameter =>
		{
			var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
			var kind = type == typeof(bool) ? OptionKind.Bool
				: type == typeof(int) ? OptionKind.Int
				: type.IsArray ? OptionKind.List
				: OptionKind.Text;
			return new OptionDef(ViewFormatter.Camel(parameter.Name!), $"The {parameter.Name} of the command.", kind,
				!parameter.HasDefaultValue && Nullable.GetUnderlyingType(parameter.ParameterType) is null &&
				!parameter.ParameterType.IsClass && !parameter.ParameterType.IsArray,
				Secret: parameter.Name!.Equals("password", StringComparison.OrdinalIgnoreCase));
		})];
	}

	/// <summary>Builds the request body of a room command from the options.</summary>
	private static object? BodyOf(CommandCall call, Type? body)
	{
		if (body is null) return null;

		var constructor = body.GetConstructors()[0];
		var arguments = constructor.GetParameters().Select(parameter =>
		{
			var name = ViewFormatter.Camel(parameter.Name!);
			var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
			if (!call.Has(name)) return parameter.ParameterType.IsValueType && Nullable.GetUnderlyingType(parameter.ParameterType) is null
				? throw CommandException.Invalid($"--{name} is required.")
				: null;
			return type == typeof(int) ? call.Int(name)
				: type == typeof(bool) ? call.Bool(name)
				: type == typeof(string) ? call.Text(name)
				: type == typeof(string[]) ? call.List(name)
				: type.IsEnum ? Enum.TryParse(type, call.Text(name), true, out var value) && Enum.IsDefined(type, value!)
					? value
					: throw CommandException.Invalid($"--{name} is not a valid {type.Name}.")
				: throw new InvalidOperationException($"No reader for {type.Name}.");
		}).ToArray();
		return constructor.Invoke(arguments);
	}

	private static Task<int> ListChannelsAsync(CommandCall call)
	{
		var registry = call.Get<GeneralChannelRegistry>();
		var views = registry.All.Values.OrderBy(channel => channel.Name).Select(channel => (object)ChannelView.Of(channel.Record)).ToList();
		call.Formatter.PrintTable(call.Output, views);
		return Task.FromResult(ExitCodes.Success);
	}

	private static GeneralChannelData DataOf(CommandCall call, GeneralChannelData? current = null)
	{
		return new GeneralChannelData
		{
			Topic = call.Text("topic") ?? current?.Topic ?? string.Empty,
			ReadPrivileges = call.Mask("readPrivileges") is { } read ? (UserPrivileges)read : current?.ReadPrivileges ?? 0,
			WritePrivileges = call.Mask("writePrivileges") is { } write ? (UserPrivileges)write : current?.WritePrivileges ?? 0,
			AutoJoin = call.Bool("autoJoin") ?? current?.AutoJoin ?? false,
			Visible = call.Bool("visible") ?? current?.Visible ?? true
		};
	}

	private static async Task<int> CreateChannelAsync(CommandCall call)
	{
		var record = new GeneralChannelRecord { Name = call.Required("name"), Value = DataOf(call) };
		var result = await call.Get<Server>().ScopeTo(await call.ByAsync()).Channels.CreateAsync(record, call.CancellationToken);
		return call.Report(result, $"Created channel {record.Name}.");
	}

	private static async Task<int> SetChannelAsync(CommandCall call)
	{
		if (call.Get<GeneralChannelRegistry>().Find(call.Word("name")) is not { } channel) return call.NotFound("channel");

		var scoped = channel.ScopeTo(await call.ByAsync()).General!;
		var result = await scoped.ConfigureAsync(new GeneralChannelChange(call.Text("topic"),
			(UserPrivileges?)call.Mask("readPrivileges"), (UserPrivileges?)call.Mask("writePrivileges"), call.Bool("autoJoin"),
			call.Bool("visible")), call.CancellationToken);
		return call.Report(result, "Updated.");
	}

	private static async Task<int> DeleteChannelAsync(CommandCall call)
	{
		return call.Get<GeneralChannelRegistry>().Find(call.Word("name")) is not { } channel
			? call.NotFound("channel")
			: call.Report(await channel.ScopeTo(await call.ByAsync()).General!.DeleteAsync(call.CancellationToken), "Channel deleted.");
	}

	private static Task<int> ListRoomsAsync(CommandCall call)
	{
		var page = call.Page();
		var rooms = call.Get<Lobby>().Rooms.Values.OrderBy(room => room.Id).ToList();
		call.PrintPage(new Page<RoomView>(
			[.. rooms.Skip(page.Offset).Take(page.Limit).Select(RoomView.Of)], rooms.Count), page);
		return Task.FromResult(ExitCodes.Success);
	}

	private static async Task<int> CreateRoomAsync(CommandCall call)
	{
		var (room, result) = await call.Get<Lobby>().ScopeTo(await call.ByAsync()).OpenRoomAsync(
			RoomCommands.Truncate(call.Required("name")), call.Text("password") ?? string.Empty, true,
			call.Bool("private") ?? false, cancellationToken: call.CancellationToken);
		if (!result.IsOk) return call.Refuse(result);

		call.Output.Ok($"Opened room {room!.Id}.");
		return ExitCodes.Success;
	}

	private static Task<int> ShowRoomAsync(CommandCall call)
	{
		if (call.Get<Lobby>().Find(call.Id("id")) is not { } room) return Task.FromResult(call.NotFound("room"));

		call.Formatter.Print(call.Output, RoomView.Of(room));
		return Task.FromResult(ExitCodes.Success);
	}

	private static async Task<int> RunRoomAsync(CommandCall call, RoomCommand command)
	{
		if (call.Get<Lobby>().Find(call.Id("id")) is not { } room) return call.NotFound("room");

		var body = BodyOf(call, command.Body);
		var result = await command.Run(room.ScopeTo(await call.ByAsync()), body, call.Services, call.CancellationToken);
		return call.Report(result, $"Done: {command.Name}.");
	}

	private static async Task<int> ListMatchesAsync(CommandCall call)
	{
		var page = call.Page();
		var (result, found) = await call.Get<Lobby>().ScopeTo(await call.ByAsync()).ListMatchesAsync(
			MatchQuery.Parse(call.Text("query") ?? string.Empty), page, call.CancellationToken);
		if (!result.IsOk) return call.Refuse(result);

		call.PrintPage(new Page<MatchView>([.. found!.Items.Select(MatchView.Of)], found.Total), page);
		return ExitCodes.Success;
	}

	private static async Task<int> ShowMatchAsync(CommandCall call)
	{
		if (await call.Get<IMatchRepository>().GetAsync(call.Id("id"), call.CancellationToken) is not { } match)
			return call.NotFound("match");

		call.Formatter.Print(call.Output, MatchView.Of(match));
		var report = await call.Get<IMatchReportRepository>().GetAsync(match, call.CancellationToken);
		var entries = report.Records.OrderBy(entry => entry.Timestamp).ToList();
		call.Output.Line($"entries: {entries.Count}");
		foreach (var entry in entries)
			call.Output.Line(entry switch
			{
				Basil.Domain.Multiplayer.Round round =>
					$"{call.Formatter.Time(entry.Timestamp)}  round {round.Index}{(round.Aborted ? " (aborted)" : "")}",
				Basil.Domain.Multiplayer.MatchEvent matchEvent =>
					$"{call.Formatter.Time(entry.Timestamp)}  {matchEvent.Type}{(matchEvent.Actor is null ? "" : $" by {matchEvent.Actor.Value.Name}")}",
				_ => entry.ToString() ?? string.Empty
			});
		return ExitCodes.Success;
	}
}

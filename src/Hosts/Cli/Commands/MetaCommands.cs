using System.Diagnostics;
using System.Reflection;
using Basil.Application.Multiplayer.Runtime;
using Basil.Application.Users.Users;
using Basil.Domain.Auth;

namespace Basil.Host.Cli.Commands;

/// <summary>What the server's process lets the console do to it.</summary>
public interface IServerControl
{
	/// <summary>Starts the graceful shutdown.</summary>
	/// <param name="restart">Whether the process ends with the restart exit code.</param>
	void RequestShutdown(bool restart);
}

/// <summary>The colour themes of the dashboard.</summary>
public interface IThemeControl
{
	/// <summary>Gets the names of the themes.</summary>
	IReadOnlyList<string> Names { get; }

	/// <summary>Gets the name of the theme in use.</summary>
	string Current { get; }

	/// <summary>Changes the theme and saves the choice.</summary>
	/// <param name="name">The name of the theme.</param>
	/// <returns><see langword="true" /> when the theme exists.</returns>
	bool Set(string name);
}

/// <summary>The commands that exist only on the console: <c>help</c>, <c>status</c>, <c>shutdown</c>, <c>restart</c> and <c>theme</c>.</summary>
internal static class MetaCommands
{
	public static IEnumerable<CommandDef> Create(Func<CommandCatalog> catalog)
	{
		yield return new CommandDef
		{
			Pattern = "help", Offline = true, NeedsStorage = false, Summary = "Lists the commands.", Run = call => HelpAsync(call, catalog(), [])
		};
		yield return new CommandDef
		{
			Pattern = "help <topic>", Offline = true, NeedsStorage = false, Summary = "Shows the syntax, options and API route of a command.",
			Run = call => HelpAsync(call, catalog(), [call.Word("topic")])
		};
		yield return new CommandDef
		{
			Pattern = "status", Summary = "Shows the version, the uptime, the connections online and the rooms.", Run = StatusAsync
		};
		foreach (var restart in new[] { false, true })
		{
			var again = restart;
			yield return new CommandDef
			{
				Pattern = again ? "restart" : "shutdown",
				Summary = again ? "Shuts the server down gracefully and asks to be started again." : "Shuts the server down gracefully.",
				Options = [new("now", "Do not ask for confirmation.", OptionKind.Bool)], Run = call => ShutdownAsync(call, again)
			};
		}

		yield return new CommandDef
		{
			Pattern = "theme", DashboardOnly = true, Summary = "Lists the colour themes.", Run = call => ThemeAsync(call, null)
		};
		yield return new CommandDef
		{
			Pattern = "theme <scheme>", DashboardOnly = true, Summary = "Changes the dashboard theme and saves it.",
			Run = call => ThemeAsync(call, call.Word("scheme"))
		};
	}

	private static Task<int> HelpAsync(CommandCall call, CommandCatalog catalog, IReadOnlyList<string> topic)
	{
		var commands = topic.Count == 0
			? catalog.All
			: [.. catalog.All.Where(command => command.Words[0].Equals(topic[0], StringComparison.OrdinalIgnoreCase))];
		if (commands.Count == 0)
		{
			call.Output.Error("notFound", $"No such command: {topic[0]}.");
			return Task.FromResult(ExitCodes.Refused);
		}

		if (topic.Count == 0)
		{
			call.Output.Table(["command", "description", "offline"],
				[.. catalog.All.Select(command => (IReadOnlyList<string>)[command.Pattern, command.Summary, command.Offline ? "yes" : "no"])]);
			return Task.FromResult(ExitCodes.Success);
		}

		foreach (var command in commands)
		{
			var options = string.Concat(command.Options.Select(o => o.Required ? $" --{o.Name} <{o.Kind.ToString().ToLowerInvariant()}>" : $" [--{o.Name}]"));
			call.Output.Line($"{command.Pattern}{options}");
			call.Output.Line($"  {command.Summary}");
			if (command.AllRoutes.Any()) call.Output.Line($"  api: {string.Join(", ", command.AllRoutes)}");
			call.Output.Line($"  runs with the server stopped: {(command.Offline ? "yes" : "no")}");
			foreach (var option in command.Options) call.Output.Line($"  --{option.Name}  {option.Description}");
		}

		return Task.FromResult(ExitCodes.Success);
	}

	private static Task<int> StatusAsync(CommandCall call)
	{
		var registry = call.Get<UserRegistry>();
		var lobby = call.Get<Lobby>();
		var now = call.Get<TimeProvider>().GetUtcNow();
		using var process = Process.GetCurrentProcess();
		var online = registry.Online.Values.SelectMany(user => user.Connections).Where(c => c.Type != SessionType.Bot).ToList();
		var fields = new List<(string, string)>
		{
			("version", Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown"),
			("uptime", (now - process.StartTime.ToUniversalTime()).ToString(@"d\.hh\:mm\:ss")),
			("users online", registry.Online.Count.ToString())
		};
		fields.AddRange(Enum.GetValues<SessionType>().Where(type => type != SessionType.Bot).Select(type =>
			($"{type.ToString().ToLowerInvariant()} connections", online.Count(c => c.Type == type).ToString())));
		fields.Add(("rooms", lobby.Rooms.Count.ToString()));
		fields.Add(("rounds in progress", lobby.Rooms.Values.Count(room => room.Rounds.InProgress).ToString()));
		call.Output.Fields(fields);
		return Task.FromResult(ExitCodes.Success);
	}

	private static async Task<int> ShutdownAsync(CommandCall call, bool restart)
	{
		var verb = restart ? "restart" : "shut down";
		if (call.Bool("now") != true)
		{
			if (call.Session.Confirm is null)
				throw CommandException.Invalid($"Confirmation is needed to {verb} the server; add --now.");

			if (!await call.Session.Confirm($"Really {verb} the server?", call.CancellationToken))
			{
				call.Output.Line("Cancelled.");
				return ExitCodes.Declined;
			}
		}

		call.Get<IServerControl>().RequestShutdown(restart);
		call.Output.Ok(restart ? "Restarting: the server is shutting down." : "The server is shutting down.");
		return ExitCodes.Success;
	}

	private static Task<int> ThemeAsync(CommandCall call, string? scheme)
	{
		if (call.Services.GetService(typeof(IThemeControl)) is not IThemeControl themes)
		{
			call.Output.Error("invalid", "There is no dashboard.");
			return Task.FromResult(ExitCodes.Invalid);
		}

		if (scheme is null)
		{
			call.Output.Line($"current: {themes.Current}");
			call.Output.Line(string.Join(", ", themes.Names));
			return Task.FromResult(ExitCodes.Success);
		}

		if (!themes.Set(scheme))
		{
			call.Output.Error("notFound", $"No such theme: {scheme}.");
			return Task.FromResult(ExitCodes.Refused);
		}

		call.Output.Ok($"Theme changed to {scheme}.");
		return Task.FromResult(ExitCodes.Success);
	}
}

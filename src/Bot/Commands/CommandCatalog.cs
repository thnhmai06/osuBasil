using Basil.Bot.Replies;

namespace Basil.Bot.Commands;

/// <summary>One chat command BasilBot answers, as the help lists it.</summary>
/// <param name="Path">The words that name the command, without the prefix (<c>roll</c>, <c>mp mods</c>).</param>
/// <param name="Usage">The arguments the command takes, in the notation of the help; empty when it takes none.</param>
/// <param name="Summary">The reply that says what the command does; the replies file can reword it.</param>
internal sealed record CommandHelp(string Path, string Usage, Reply Summary);

/// <summary>Every chat command BasilBot answers; <c>!help</c>, <c>!mp help</c> and the command reference are made from it.</summary>
internal static class CommandCatalog
{
	/// <summary>The commands, in the order the help lists them.</summary>
	public static readonly IReadOnlyList<CommandHelp> All =
	[
		Of("help", "", "list the commands"),
		Of("roll", "[max]", "roll a random number from 0 to max (default 100)"),
		Of("where", "<username>", "show a user's country"),
		Of("faq", "<entry>|list", "print a FAQ entry, or list every entry"),
		Of("mp", "help", "list multiplayer subcommands"),
		Of("channel", "list|create|set|delete", "manage the general channels (needs permission)"),

		Of("mp make", "[name]", "create a public match and become its creator"),
		Of("mp makeprivate", "[name]", "create a private match and become its creator"),
		Of("mp join", "<id> [password]", "join a match"),
		Of("mp in", "[match_id]", "target a match from a private message; omit the id to clear the target"),
		Of("mp settings", "", "show match id, map, team type, win condition, mods, and slots"),
		Of("mp lock", "", "lock the slots, so players cannot change slot or team"),
		Of("mp unlock", "", "unlock the room"),
		Of("mp private", "[0|1]", "show or set the room's private status (hidden from the lobby and the match lists)"),
		Of("mp size", "<1-16>", "set the number of available slots"),
		Of("mp move", "<name/id> <slot 1-16>", "move a user to another slot"),
		Of("mp host", "<name/id>", "transfer host to another user"),
		Of("mp clearhost", "", "clear the current host"),
		Of("mp name", "<text>", "rename the match"),
		Of("mp password", "[text]", "set the room password; omit to clear it"),
		Of("mp invite", "<name/id>", "invite an online user"),
		Of("mp addref", "<name/id>", "add a referee (creator only)"),
		Of("mp removeref", "<name/id>", "remove a referee (creator only)"),
		Of("mp listrefs", "", "list current referees"),
		Of("mp banlist", "", "list players banned from this match"),
		Of("mp team", "<name/id> <red|blue>", "assign a user's team"),
		Of("mp map", "<beatmap id> [playmode]", "change the selected map"),
		Of("mp mods", "<mods>|Freemod|None", "set the match mods"),
		Of("mp set", "<teammode 0-3> [scoremode 0-3] [size 1-16]", "set team type, win condition, and size at once"),
		Of("mp start", "[seconds]", "start now, or after a countdown"),
		Of("mp timer", "[seconds]", "start a countdown without auto-starting"),
		Of("mp aborttimer", "", "cancel a running countdown"),
		Of("mp abort", "", "abort the match in progress"),
		Of("mp kick", "<name/id>", "remove a user from the room"),
		Of("mp ban", "<name/id>", "kick and block a user from rejoining"),
		Of("mp unban", "<name/id>", "allow a banned user to rejoin"),
		Of("mp close", "", "close the match immediately")
	];

	/// <summary>Says the lines of the help for the commands one step below a scope.</summary>
	/// <param name="replies">Gives the wording of each summary.</param>
	/// <param name="scope">The command whose subcommands are listed, or empty for the commands of the prefix itself.</param>
	/// <returns>One line per command: <c>!path usage - summary</c>.</returns>
	public static string Lines(IReplies replies, string scope)
	{
		var depth = scope.Length == 0 ? 0 : scope.Count(c => c == ' ') + 1;
		return string.Join('\n', All
			.Where(command => command.Path.Count(c => c == ' ') == depth &&
			                  (scope.Length == 0 || command.Path.StartsWith(scope + " ", StringComparison.Ordinal)))
			.Select(command =>
				$"!{command.Path}{(command.Usage.Length == 0 ? "" : " " + command.Usage)} - {replies.Text(command.Summary)}"));
	}

	private static CommandHelp Of(string path, string usage, string summary)
	{
		var name = path.Split(' ')[^1];
		var surface = path.StartsWith("mp ", StringComparison.Ordinal) ? "mp" : "bot";
		return new CommandHelp(path, usage,
			new Reply($"{surface}.help{char.ToUpperInvariant(name[0])}{name[1..]}", summary));
	}
}
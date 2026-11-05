using System.Text;
using Basil.Application.Bot.Commands;

namespace Basil.Application.Bot;

/// <summary>Writes the reference of BasilBot's chat commands.</summary>
public static class BotReference
{
	/// <summary>Writes the reference of the chat commands, with the wording they have unless the replies file changes it.</summary>
	/// <returns>The Markdown text.</returns>
	public static string Markdown()
	{
		var text = new StringBuilder();
		text.AppendLine("# BasilBot commands").AppendLine();
		text.AppendLine(
			"Type a command in any channel, or send it in a private message to BasilBot (the `!` is then optional). " +
			"A command runs with your own rights, as in the osu! (stable) client. `!help` and `!mp help` answer in " +
			"private messages. Unknown commands are not answered.").AppendLine();
		Table(text, "Commands", command => !command.Path.Contains(' '));
		Table(text, "Multiplayer commands (`!mp`)",
			command => command.Path.StartsWith("mp ", StringComparison.Ordinal));
		return text.ToString();
	}

	private static void Table(StringBuilder text, string title, Func<CommandHelp, bool> where)
	{
		text.AppendLine($"## {title}").AppendLine();
		text.AppendLine("| Command | What it does |").AppendLine("| --- | --- |");
		foreach (var command in CommandCatalog.All.Where(where))
			text.AppendLine(
				$"| `!{command.Path}{(command.Usage.Length == 0 ? "" : " " + command.Usage)}` | {command.Summary.Default.Replace("|", "\\|")} |");
		text.AppendLine();
	}
}
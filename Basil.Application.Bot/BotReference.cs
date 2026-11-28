using System.Text;
using Basil.Application.Bot.Commands;
using Basil.Application.Shared.Replies;

namespace Basil.Application.Bot;

public static class BotReference
{
	public static string Markdown()
	{
		var text = new StringBuilder();
		text.AppendLine("# BasilBot commands").AppendLine();
		text.AppendLine(
			"Type a command in any channel, or send it in a private message to BasilBot (the `!` is then optional). " +
			"A command runs with your own privileges, as in the osu! (stable) client. `!help` and `!mp help` answer in " +
			"private messages. An unknown command is answered with a pointer to `!help` in a private message or in a room " +
			"channel, and not at all in a public channel.").AppendLine();
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
				$"| `!{command.Path}{(command.Usage.Length == 0 ? "" : " " + command.Usage)}` | {ReplyAttribute.Of(command.Summary).Text.Replace("|", "\\|")} |");
		text.AppendLine();
	}
}
using Basil.Application.Shared.Replies;
using Basil.Application.Bot.Replies;

namespace Basil.Application.Bot.Commands;

internal sealed record CommandHelp(string Path, string Usage, BotReply Summary);

internal static class CommandCatalog
{
	public static readonly IReadOnlyList<CommandHelp> All =
	[
		Of("help", "", BotReply.BotHelpHelp),
		Of("roll", "[max]", BotReply.BotHelpRoll),
		Of("where", "<username>", BotReply.BotHelpWhere),
		Of("faq", "<entry>|list", BotReply.BotHelpFaq),
		Of("mp", "help", BotReply.BotHelpMp),
		Of("channel", "list|create|set|delete", BotReply.BotHelpChannel),

		Of("mp make", "[name]", BotReply.MpHelpMake),
		Of("mp makeprivate", "[name]", BotReply.MpHelpMakeprivate),
		Of("mp join", "<id> [password]", BotReply.MpHelpJoin),
		Of("mp in", "[room_id]", BotReply.MpHelpIn),
		Of("mp settings", "", BotReply.MpHelpSettings),
		Of("mp lock", "", BotReply.MpHelpLock),
		Of("mp unlock", "", BotReply.MpHelpUnlock),
		Of("mp private", "[0|1]", BotReply.MpHelpPrivate),
		Of("mp size", "<2-16>", BotReply.MpHelpSize),
		Of("mp move", "<name/id> <slot 1-16>", BotReply.MpHelpMove),
		Of("mp host", "<name/id>", BotReply.MpHelpHost),
		Of("mp clearhost", "", BotReply.MpHelpClearhost),
		Of("mp name", "<text>", BotReply.MpHelpName),
		Of("mp password", "[text]", BotReply.MpHelpPassword),
		Of("mp invite", "<name/id>", BotReply.MpHelpInvite),
		Of("mp addref", "<name/id>", BotReply.MpHelpAddref),
		Of("mp removeref", "<name/id>", BotReply.MpHelpRemoveref),
		Of("mp listrefs", "", BotReply.MpHelpListrefs),
		Of("mp banlist", "", BotReply.MpHelpBanlist),
		Of("mp team", "<name/id> <red|blue>", BotReply.MpHelpTeam),
		Of("mp map", "<beatmap id> [playmode]", BotReply.MpHelpMap),
		Of("mp mods", "<mods>|Freemod|None", BotReply.MpHelpMods),
		Of("mp set", "<teammode 0-3> [scoremode 0-3] [size 2-16]", BotReply.MpHelpSet),
		Of("mp start", "[seconds]", BotReply.MpHelpStart),
		Of("mp timer", "[seconds]", BotReply.MpHelpTimer),
		Of("mp aborttimer", "", BotReply.MpHelpAborttimer),
		Of("mp abort", "", BotReply.MpHelpAbort),
		Of("mp kick", "<name/id>", BotReply.MpHelpKick),
		Of("mp ban", "<name/id>", BotReply.MpHelpBan),
		Of("mp unban", "<name/id>", BotReply.MpHelpUnban),
		Of("mp close", "", BotReply.MpHelpClose)
	];

	public static string Lines(IReplies<BotReply> replies, string scope)
	{
		var depth = scope.Length == 0 ? 0 : scope.Count(c => c == ' ') + 1;
		return string.Join('\n', All
			.Where(command => command.Path.Count(c => c == ' ') == depth &&
			                  (scope.Length == 0 || command.Path.StartsWith(scope + " ", StringComparison.Ordinal)))
			.Select(command =>
				$"!{command.Path}{(command.Usage.Length == 0 ? "" : " " + command.Usage)} - {replies.Text(command.Summary)}"));
	}

	public static string? Line(IReplies<BotReply> replies, string path)
	{
		return All.FirstOrDefault(command => command.Path == path) is { } found
			? $"!{found.Path}{(found.Usage.Length == 0 ? "" : " " + found.Usage)} - {replies.Text(found.Summary)}"
			: null;
	}

	private static CommandHelp Of(string path, string usage, BotReply summary)
	{
		return new CommandHelp(path, usage, summary);
	}
}
namespace Basil.Host.Cli.Commands;

public static class CommandCatalogFactory
{
	public static CommandCatalog Create()
	{
		CommandCatalog? catalog = null;
		var commands = new List<CommandDef>();
		commands.AddRange(MetaCommands.Create(() => catalog!));
		commands.AddRange(UserCommands.Create());
		commands.AddRange(ChannelRoomCommands.Create());
		commands.AddRange(BeatmapScoreCommands.Create());
		commands.AddRange(ContentCommands.Create());
		commands.AddRange(RulesetCommands.Create());
		return catalog = new CommandCatalog(commands);
	}
}
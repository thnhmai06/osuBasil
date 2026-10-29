namespace Basil.Host.Cli.Commands;

/// <summary>Builds the catalog of every console command.</summary>
public static class CommandCatalogFactory
{
	/// <summary>Creates the catalog with all commands.</summary>
	public static CommandCatalog Create()
	{
		CommandCatalog? catalog = null;
		var commands = new List<CommandDef>();
		commands.AddRange(MetaCommands.Create(() => catalog!));
		commands.AddRange(UserCommands.Create());
		commands.AddRange(ChannelRoomCommands.Create());
		commands.AddRange(BeatmapScoreCommands.Create());
		commands.AddRange(ContentCommands.Create());
		return catalog = new CommandCatalog(commands);
	}
}

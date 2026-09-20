namespace Basil.Application.Options.Mutable;

public partial record BasilSettings(
	BasilSettings.HostSettings Host,
	BasilSettings.BotSettings Bot,
	BasilSettings.UpdateSettings Update) : ISettings
{
	public static string GetSectionName()
	{
		return "Basil";
	}
}
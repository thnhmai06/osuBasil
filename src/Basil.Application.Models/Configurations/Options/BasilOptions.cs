namespace Basil.Application.Models.Configurations.Options;

public partial record BasilOptions(
	BasilOptions.HostOptions Host,
	BasilOptions.BotOptions Bot,
	BasilOptions.UpdateOptions Update) : IConfiguration
{
	public static string GetSectionName()
	{
		return "Basil";
	}
}
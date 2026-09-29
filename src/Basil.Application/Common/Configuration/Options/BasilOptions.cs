using Basil.Application.Common.Configuration;
namespace Basil.Application.Common.Configuration.Options;

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
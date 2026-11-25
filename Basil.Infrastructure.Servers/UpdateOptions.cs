namespace Basil.Infrastructure.Servers;

public sealed class UpdateOptions
{
	public bool CheckOnStartup { get; set; } = true;

	public string Source { get; set; } = "https://github.com/thnhmai06/osuBasil";
}

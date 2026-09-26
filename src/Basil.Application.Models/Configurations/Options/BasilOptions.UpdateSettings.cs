namespace Basil.Application.Models.Configurations.Options;

public partial record BasilOptions
{
	/// <summary>
	///     Settings controlling whether the server looks for a newer release and where it looks.
	/// </summary>
	public sealed record UpdateOptions(
		bool CheckOnStartup = true,
		string Source = "https://github.com/thnhmai06/osuBasil") : IConfiguration
	{
		public static string GetSectionName()
		{
			return BasilOptions.GetSectionName() + ":Update";
		}
	}
}
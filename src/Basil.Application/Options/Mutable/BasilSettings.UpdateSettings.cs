namespace Basil.Application.Options.Mutable;

public partial record BasilSettings
{
	/// <summary>
	///     Settings controlling whether the server looks for a newer release and where it looks.
	/// </summary>
	public sealed record UpdateSettings(
		bool CheckOnStartup = true,
		string Source = "https://github.com/thnhmai06/osuBasil") : ISettings
	{
		public static string GetSectionName()
		{
			return BasilSettings.GetSectionName() + ":Update";
		}
	}
}
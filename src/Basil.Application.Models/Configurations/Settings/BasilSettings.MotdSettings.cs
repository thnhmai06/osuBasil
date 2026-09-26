namespace Basil.Application.Models.Configurations.Settings;

public partial class BasilSettings
{
	/// <summary>The message-of-the-day text shown to a player at login and by the IRC <c>/MOTD</c> command.</summary>
	/// <param name="Text">The MOTD text, or <see langword="null" /> when none is configured.</param>
	public sealed class MotdSettings(string? Text = null) : IConfiguration
	{
		public string? Text { get; set; } = Text;

		/// <inheritdoc />
		public static string GetSectionName()
		{
			return BasilSettings.GetSectionName() + ":Motd";
		}
	}
}
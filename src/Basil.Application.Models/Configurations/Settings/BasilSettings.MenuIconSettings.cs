namespace Basil.Application.Models.Configurations.Settings;

public partial class BasilSettings
{
	/// <summary>The in-game main menu icon and its click-through URL.</summary>
	/// <param name="IconPath">A local file path or external URL for the icon, or <see langword="null" /> if unset.</param>
	/// <param name="ClickUrl">The URL opened when the icon is clicked, or <see langword="null" /> if unset.</param>
	public sealed class MenuIconSettings : IConfiguration
	{
		public string? IconPath { get; set; } = null;

		public string? ClickUrl { get; set; } = null;

		public bool IsEnabled => IconPath != null;

		/// <inheritdoc />
		public static string GetSectionName()
		{
			return BasilSettings.GetSectionName() + ":MenuIcon";
		}
	}
}
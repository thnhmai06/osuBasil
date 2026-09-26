namespace Basil.Application.Models.Configurations.Settings;

public partial class BasilSettings
{
	/// <summary>The server's configured beatmap mirror endpoints.</summary>
	/// <param name="DownloadEndpoint">The mirror that serves .osz downloads, or <see langword="null" /> if unset.</param>
	/// <param name="SearchEndpoint">The mirror that serves osu!direct search, or <see langword="null" /> if unset.</param>
	public sealed class MirrorSettings : IConfiguration
	{
		public string? DownloadEndpoint { get; set; } = null;

		public string? SearchEndpoint { get; set; } = null;

		/// <inheritdoc />
		public static string GetSectionName()
		{
			return BasilSettings.GetSectionName() + ":Mirror";
		}
	}
}
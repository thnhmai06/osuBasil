namespace Basil.Application.Options.Immutable;

public static class BasilConfigurations
{
	public static class Storage
	{
		private const string Folder = "Data";

		public static readonly string SettingsPath = Path.Combine(Folder, "appsettings.json");

		public static readonly string DatabasePath = Path.Combine(Folder, "Basil.db");

		/// <summary>Gets or sets the folder that stores replay files.</summary>
		public static readonly string ReplaysPath = Path.Combine(Folder, "Replays");

		/// <summary>Gets or sets the folder that stores user avatar files.</summary>
		public static readonly string AvatarsPath = Path.Combine(Folder, "Avatars");

		/// <summary>Gets or sets the folder that stores uploaded beatmap set files (.osz).</summary>
		public static readonly string BeatmapsPath = Path.Combine(Folder, "Beatmaps");

		public static class Menu
		{
			private static readonly string Folder = Path.Combine(Storage.Folder, "Menu");

			/// <summary>Gets or sets the folder that stores seasonal background files.</summary>
			public static readonly string SeasonalsPath = Path.Combine(Folder, "Seasonals");

			/// <summary>Gets or sets the folder that stores main-menu banner image files.</summary>
			public static readonly string BannersPath = Path.Combine(Folder, "Banners");
		}

		/// <summary>Gets or sets the folder that stores FAQ entry files.</summary>
		public static readonly string FaqsPath = Path.Combine(Folder, "Faqs");

		public static readonly string CachePath = Path.Combine(Folder, "Caches");

		public static readonly string LocalizationPath = Path.Combine(Folder, "Localizations");
	}
}
namespace Basil.Application.Models.Configurations.Settings;

public partial class BasilSettings
{
	/// <summary>
	///     The server's admin key, gating management actions and in-game registration. An unset
	///     <see cref="Hash" /> puts the server in bypass mode: every admin-gated action succeeds without
	///     a key.
	/// </summary>
	/// <param name="Hash">The stored hash of the admin key, or <see langword="null" /> in bypass mode.</param>
	/// <param name="LastChanged">When the key was last set or cleared, or <see langword="null" /> if never.</param>
	public sealed class AdminKeySettings : IConfiguration
	{
		public string? Hash { get; set; } = null;

		public DateTimeOffset? LastChanged { get; set; } = null;

		public bool IsSet => Hash != null;

		/// <inheritdoc />
		public static string GetSectionName()
		{
			return BasilSettings.GetSectionName() + ":AdminKey";
		}
	}
}
namespace Basil.Application.Options.Mutable;

public partial record BasilSettings
{
	/// <summary>
	///     Core server configuration: the host domain, the HTTPS listen port, and the TLS certificate.
	/// </summary>
	/// <remarks>
	///     <see cref="Domain" /> is the apex domain this server's subdomains respond under. There is no
	///     static menu-icon or menu-click URL setting here; see
	///     <see cref="MenuIconService" /> for the
	///     runtime-configurable, database-backed replacement.
	/// </remarks>
	public sealed record HostSettings(
		HostSettings.BanchoSettings Bancho,
		HostSettings.IrcSettings Irc,
		bool AdvertiseDomain = true)
		: ISettings
	{
		public static string GetSectionName()
		{
			return BasilSettings.GetSectionName() + ":Host";
		}

		public sealed record BanchoSettings(
			string Domain = "basil.local",
			int Port = 443,
			BanchoSettings.CertificateSettings? Certificate = null)
			: ISettings
		{
			public static string GetSectionName()
			{
				return HostSettings.GetSectionName() + ":Bancho";
			}

			public sealed record CertificateSettings(string Path, string? Password = null) : ISettings
			{
				public static string GetSectionName()
				{
					return BanchoSettings.GetSectionName() + ":Certificate";
				}
			}
		}

		/// <summary>
		///     Configuration for the embedded IRC gateway.
		/// </summary>
		/// <remarks>
		///     Chat arriving over IRC is routed through the same channel and dispatch services as the osu!
		///     binary protocol, so both transports share a single chat core (see docs/for-developers/chat.md).
		/// </remarks>
		public sealed record IrcSettings(string ServerName = "Basil", int Port = 6667) : ISettings
		{
			public static string GetSectionName()
			{
				return HostSettings.GetSectionName() + ":Irc";
			}
		}
	}
}
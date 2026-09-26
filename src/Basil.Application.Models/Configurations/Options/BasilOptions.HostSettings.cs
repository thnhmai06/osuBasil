namespace Basil.Application.Models.Configurations.Options;

public partial record BasilOptions
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
	public sealed record HostOptions(
		HostOptions.BanchoOptions Bancho,
		HostOptions.IrcOptions Irc,
		bool AdvertiseDomain = true)
		: IConfiguration
	{
		public static string GetSectionName()
		{
			return BasilOptions.GetSectionName() + ":Host";
		}

		public sealed record BanchoOptions(
			string Domain = "basil.local",
			int Port = 443,
			BanchoOptions.CertificateSettings? Certificate = null)
			: IConfiguration
		{
			public static string GetSectionName()
			{
				return HostOptions.GetSectionName() + ":Bancho";
			}

			public sealed record CertificateSettings(string Path, string? Password = null) : IConfiguration
			{
				public static string GetSectionName()
				{
					return BanchoOptions.GetSectionName() + ":Certificate";
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
		public sealed record IrcOptions(string ServerName = "Basil", int Port = 6667) : IConfiguration
		{
			public static string GetSectionName()
			{
				return HostOptions.GetSectionName() + ":Irc";
			}
		}
	}
}
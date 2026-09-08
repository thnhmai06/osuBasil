namespace Basil.Server.Shared.Configuration;

/// <summary>
///     Core server configuration: the host domain, the HTTPS listen port, and the TLS certificate.
/// </summary>
/// <remarks>
///     <see cref="Domain" /> is the apex domain this server's subdomains respond under. There is no
///     static menu-icon or menu-click URL setting here; see
///     <see cref="Basil.Server.Features.Content.MenuIconService" /> for the
///     runtime-configurable, database-backed replacement.
/// </remarks>
public sealed class ServerOptions
{
	public const string SectionName = "Basil:Server";

	/// <summary>Gets or sets the apex domain this server responds on.</summary>
	public required string Domain { get; init; }

	/// <summary>Gets or sets the Kestrel HTTPS listen port.</summary>
	/// <remarks>Configuring this value disables automatic port selection.</remarks>
	/// <value>Defaults to <c>443</c>.</value>
	public int Port { get; init; } = 443;

	/// <summary>Gets or sets the path to the HTTPS certificate file (PFX).</summary>
	public string? CertPath { get; init; }

	/// <summary>Gets or sets the password that decrypts the HTTPS certificate.</summary>
	public string? CertPassword { get; init; }

	/// <summary>
	///     Whether the server answers multicast DNS queries for <see cref="Domain" /> and its
	///     subdomains, so that clients on the same network resolve them without a hosts entry.
	/// </summary>
	/// <remarks>
	///     Only <see cref="Domain" /> is advertised. The server also serves the equivalent
	///     <c>ppy.sh</c> hosts, but claiming those on a shared network would redirect traffic that is
	///     not this server's to answer for, so they are never advertised.
	/// </remarks>
	public bool AdvertiseDomain { get; init; } = true;
}
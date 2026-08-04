namespace Basil.Application.Configurations;

/// <summary>
///     Core server configuration: the host domain, the HTTPS listen port, and the TLS certificate.
/// </summary>
/// <remarks>
///     <see cref="Domain" /> is the apex domain this server's subdomains respond under. There is no
///     static menu-icon or menu-click URL setting here; see
///     <see cref="Basil.Application.Services.Content.MenuIconService" /> for the
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
}
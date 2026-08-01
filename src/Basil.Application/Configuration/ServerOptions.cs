namespace Basil.Application.Configuration;

/// <summary>
///     Core server configuration: the host domain, the HTTPS listen port, and the TLS certificate.
/// </summary>
/// <remarks>
///     <see cref="Domain" /> is the apex domain this server's subdomains respond under. There is no
///     static menu-icon or menu-click URL setting here; see
///     <see cref="Basil.Application.Services.Content.MenuIconService" /> for the
///     runtime-configurable, file-backed replacement.
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

	/// <summary>Gets or sets the secret that gates management actions and in-game registration.</summary>
	/// <remarks>
	///     Management actions (beatmap, user, replay, match, and seasonal CRUD) require callers to
	///     present it. The in-game registration flow also uses it as the secret a client must send
	///     in the Email field to self-register. Leave it unset to reject all management actions and
	///     to disable in-game registration.
	/// </remarks>
	public string? AdminKey { get; init; }
}
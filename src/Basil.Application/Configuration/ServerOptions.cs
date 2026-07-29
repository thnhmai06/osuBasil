namespace Basil.Application.Configuration;

/// <summary>
///     Ports DOMAIN from app/settings.py. MENU_ICON_URL/MENU_ONCLICK_URL have no equivalent here —
///     see <see cref="Basil.Application.Services.Content.MenuIconService" /> for the runtime-configurable,
///     file-backed replacement.
/// </summary>
public sealed class ServerOptions
{
	public const string SectionName = "Basil:Server";

	public required string Domain { get; init; }

	/// <summary>Kestrel HTTPS listen port. Disables automatic port selection.</summary>
	public int Port { get; init; } = 443;

	/// <summary>Path to the HTTPS certificate file (PFX).</summary>
	public string? CertPath { get; init; }

	/// <summary>Password for the HTTPS certificate.</summary>
	public string? CertPassword { get; init; }

	/// <summary>
	///     Gates every api.&lt;domain&gt; management REST route (beatmap/user/replay/match/seasonal CRUD)
	///     via the X-Admin-Key header. Also used by the in-game registration endpoint (osu. POST /users)
	///     as the secret the client must send in the Email field to self-register.
	///     Leave unset to lock management routes down (401) and disable in-game registration.
	/// </summary>
	public string? AdminKey { get; init; }
}
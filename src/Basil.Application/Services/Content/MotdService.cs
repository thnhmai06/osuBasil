using Basil.Application.Abstractions.Settings;

namespace Basil.Application.Services.Content;

/// <summary>
///     Stores the message-of-the-day text shown to a player as a login notification and by the IRC
///     gateway's <c>/MOTD</c> command.
/// </summary>
public sealed class MotdService(ISettingsRepository settings)
{
	// Matches the "Motd" row already seeded by 001_base.sql -- SqliteSettingsRepository.SetAsync is
	// UPDATE-only against a pre-seeded row, so a different key here would silently no-op every write
	// against a real deployment.
	private const string TextSettingKey = "Motd";

	/// <summary>Gets the current MOTD text, or <see langword="null" /> when none is configured.</summary>
	/// <param name="cancellationToken">A token that cancels the read.</param>
	public async Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
	{
		var text = await settings.GetAsync(TextSettingKey, cancellationToken);
		return string.IsNullOrEmpty(text) ? null : text;
	}

	/// <summary>Sets the MOTD text, clearing it when <paramref name="text" /> is null, empty, or blank.</summary>
	/// <param name="text">The new MOTD text.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	public async Task SetTextAsync(string? text, CancellationToken cancellationToken = default)
	{
		var trimmed = text?.Trim();
		await settings.SetAsync(TextSettingKey, string.IsNullOrEmpty(trimmed) ? null : trimmed, cancellationToken);
	}
}
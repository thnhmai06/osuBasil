using Basil.Application.Abstractions.Settings;

namespace Basil.Application.Services.Content;

/// <summary>
///     Stores the message-of-the-day text shown to a player on login.
/// </summary>
public sealed class MotdService(ISettingsRepository settings)
{
	private const string TextKey = "Motd";

	/// <summary>Gets the current MOTD text, or <see langword="null" /> when none is configured.</summary>
	/// <param name="cancellationToken">A token that cancels the read.</param>
	public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
	{
		return settings.GetAsync(TextKey, cancellationToken);
	}

	/// <summary>Sets the MOTD text, clearing it when <paramref name="text" /> is null, empty, or blank.</summary>
	/// <param name="text">The new MOTD text.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	public Task SetTextAsync(string? text, CancellationToken cancellationToken = default)
	{
		var trimmed = text?.Trim();
		return settings.SetAsync(TextKey, string.IsNullOrEmpty(trimmed) ? null : trimmed, cancellationToken);
	}
}
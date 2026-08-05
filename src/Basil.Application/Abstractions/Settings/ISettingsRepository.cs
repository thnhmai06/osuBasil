namespace Basil.Application.Abstractions.Settings;

/// <summary>
///     Reads and writes arbitrary key/value configuration rows that need to change at runtime
///     without a restart, unlike the fixed values bound from <c>appsettings.json</c> at startup.
/// </summary>
public interface ISettingsRepository
{
	/// <summary>Gets the stored value for <paramref name="key" />, or <see langword="null" /> if unset.</summary>
	/// <param name="key">The setting's key.</param>
	/// <param name="cancellationToken">A token that cancels the read.</param>
	/// <returns>The stored value, or <see langword="null" /> if the key is unset.</returns>
	Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

	/// <summary>Stores <paramref name="value" /> for <paramref name="key" />, replacing any existing value.</summary>
	/// <param name="key">The setting's key.</param>
	/// <param name="value">The value to store, or <see langword="null" /> to clear it.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	Task SetAsync(string key, string? value, CancellationToken cancellationToken = default);
}
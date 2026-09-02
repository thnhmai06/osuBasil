using System.Globalization;
using System.Text;
using Basil.Application.Abstractions.Settings;
using Basil.Application.Abstractions.Users;

namespace Basil.Application.Services.Authentication;

/// <summary>
///     Manages the server's admin key: the secret that gates management actions and in-game
///     registration, stored as a bcrypt hash rather than a config-file plaintext value.
/// </summary>
/// <remarks>
///     An unset hash puts the server in bypass mode: every admin-gated action succeeds without a
///     key, until an operator sets one explicitly via <see cref="SetKeyAsync" />.
/// </remarks>
public sealed class AdminKeyService(ISettingsRepository settings, IPasswordHasher hasher)
{
	/// <summary>The longest key bcrypt hashes without silently truncating it.</summary>
	public const int MaxKeyLengthBytes = 72;

	private const string HashSettingKey = "AdminKey:Hash";
	private const string LastChangedSettingKey = "AdminKey:LastChanged";

	/// <summary>Gets whether the server is in bypass mode (no admin key hash configured).</summary>
	/// <param name="cancellationToken">A token that cancels the read.</param>
	/// <returns><see langword="true" /> if no admin key hash is stored; otherwise, <see langword="false" />.</returns>
	public async Task<bool> IsBypassAsync(CancellationToken cancellationToken = default)
	{
		var hash = await settings.GetAsync(HashSettingKey, cancellationToken);
		return string.IsNullOrWhiteSpace(hash);
	}

	/// <summary>Verifies a candidate key against the stored hash.</summary>
	/// <param name="candidateKey">The key to verify.</param>
	/// <param name="cancellationToken">A token that cancels the read and verification.</param>
	/// <returns>
	///     <see langword="true" /> if the key matches the stored hash; otherwise <see langword="false" />
	///     (including when the server is in bypass mode, since there is nothing to verify against).
	/// </returns>
	public async Task<bool> VerifyAsync(string candidateKey, CancellationToken cancellationToken = default)
	{
		var hash = await settings.GetAsync(HashSettingKey, cancellationToken);
		return !string.IsNullOrWhiteSpace(hash) && hasher.Verify(Encoding.UTF8.GetBytes(candidateKey), hash);
	}

	/// <summary>Gets when the admin key was last set or cleared, or <see langword="null" /> if never.</summary>
	/// <param name="cancellationToken">A token that cancels the read.</param>
	public async Task<DateTimeOffset?> GetLastChangedAsync(CancellationToken cancellationToken = default)
	{
		var raw = await settings.GetAsync(LastChangedSettingKey, cancellationToken);
		return string.IsNullOrWhiteSpace(raw) ? null : DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture);
	}

	/// <summary>Hashes and stores a new admin key, taking the server out of bypass mode.</summary>
	/// <param name="newKey">The plaintext key to hash and store.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	public async Task SetKeyAsync(string newKey, CancellationToken cancellationToken = default)
	{
		var hash = hasher.Hash(Encoding.UTF8.GetBytes(newKey));
		await settings.SetAsync(HashSettingKey, hash, cancellationToken);
		await StampLastChangedAsync(cancellationToken);
	}

	/// <summary>Clears the stored hash, putting the server into bypass mode immediately.</summary>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	public async Task ClearAsync(CancellationToken cancellationToken = default)
	{
		await settings.SetAsync(HashSettingKey, null, cancellationToken);
		await StampLastChangedAsync(cancellationToken);
	}

	/// <summary>
	///     Writes the current time to <see cref="LastChangedSettingKey" /> through the same
	///     <see cref="ISettingsRepository" /> every other read/write goes through.
	/// </summary>
	/// <remarks>
	///     The base migration also stamps this same column via a SQL trigger on
	///     <see cref="HashSettingKey" />'s row, as a safety net for a write that bypasses this service
	///     entirely (a manual DB edit). That trigger's write is invisible to
	///     <c>CachingSettingsRepository</c>, which only invalidates the key it was actually asked to
	///     write — leaving a stale cached <see cref="LastChangedSettingKey" /> behind after every key
	///     change until its TTL expires. Writing it explicitly here, through the same repository call
	///     every other setting change uses, invalidates its cache entry correctly.
	/// </remarks>
	private async Task StampLastChangedAsync(CancellationToken cancellationToken)
	{
		await settings.SetAsync(LastChangedSettingKey,
			DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), cancellationToken);
	}
}
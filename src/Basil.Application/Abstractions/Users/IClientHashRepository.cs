using Basil.Domain.Users;

namespace Basil.Application.Abstractions.Users;

/// <summary>
///     Provides access to the ClientHashes table, scoped to what login needs.
/// </summary>
/// <remarks>
///     Records a hardware fingerprint at login and supports the shared-hardware check that looks
///     for other accounts signing in from the same machine.
/// </remarks>
public interface IClientHashRepository
{
	/// <summary>
	///     Records a hardware fingerprint for a user and returns it as persisted.
	/// </summary>
	/// <param name="userId">The id of the user to record the fingerprint for.</param>
	/// <param name="osuPathMd5">The md5 of the client's osu! installation path.</param>
	/// <param name="adapters">A fingerprint of the machine's network adapters.</param>
	/// <param name="uninstallId">The client's uninstall id, a per-installation GUID.</param>
	/// <param name="diskSerial">A fingerprint of the primary disk's serial number.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The persisted fingerprint with its updated timestamp and occurrence count.</returns>
	/// <remarks>
	///     This is an upsert keyed on the full fingerprint: the first login inserts a row with one
	///     occurrence, and every later login with the same fingerprint bumps the occurrence count
	///     and refreshes the last-seen time.
	/// </remarks>
	Task<ClientHash> CreateAsync(int userId, string osuPathMd5, string adapters, string uninstallId,
		string diskSerial, CancellationToken cancellationToken = default);

	/// <summary>
	///     Finds users other than the given one whose recorded hardware overlaps the supplied
	///     fingerprint.
	/// </summary>
	/// <param name="userId">The id of the user to exclude from the results.</param>
	/// <param name="runningUnderWine">
	///     <see langword="true" /> to match only on the uninstall id, since adapter and disk
	///     fingerprints are unreliable under Wine; otherwise, <see langword="false" /> to treat a
	///     match on any of adapters, uninstall id, or disk serial as shared hardware.
	/// </param>
	/// <param name="adapters">A fingerprint of the machine's network adapters.</param>
	/// <param name="uninstallId">The client's uninstall id, a per-installation GUID.</param>
	/// <param name="diskSerial">A fingerprint of the primary disk's serial number, or <see langword="null" />.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Every other user sharing hardware with the supplied fingerprint, with their names and privileges.</returns>
	Task<IReadOnlyList<PlayerClientHash>> FetchAnyHardwareMatchesForUserAsync(
		int userId,
		bool runningUnderWine,
		string adapters,
		string uninstallId,
		string? diskSerial,
		CancellationToken cancellationToken = default);
}
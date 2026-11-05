using Basil.Application.Authentication.Models;
using Basil.Application.Common;
using Basil.Domain.Authentication;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;

namespace Basil.Application.Authentication.Persistence;

/// <summary>Stores the sessions of users: their history, and the tokens and keys that open them.</summary>
/// <remarks>
///     The history is kept for audit; staff read it back, and each read is itself logged to the moderation log by the
///     caller. Tokens and keys are stored only as hashes.
/// </remarks>
public interface ISessionRepository
{
	Task CreateOrUpdateAsync(SessionRecord session, CancellationToken cancellationToken = default);

	/// <summary>Lists the sessions of a user, newest first.</summary>
	/// <param name="user">The user whose sessions to list.</param>
	/// <param name="sessionQuery">Which sessions to include.</param>
	/// <param name="page">Which part of the listing to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>One part of the user's sessions, newest first, with how many sessions match in total.</returns>
	ValueTask<Page<SessionRecord>> ListAsync(
		SessionQuery sessionQuery, PageRequest page,
		CancellationToken cancellationToken = default);

	Task<SessionRecord> GetAsync(Sha256 tokenHash, CancellationToken cancellationToken = default);

	Task DeleteAsync(SessionRecord session, CancellationToken cancellationToken = default);

	Task DeleteAllAsync(UserRecord user, SessionRecord? excluded = null, CancellationToken cancellationToken = default);
}
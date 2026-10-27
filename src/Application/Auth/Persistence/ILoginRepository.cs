using Basil.Application.Common;
using Basil.Domain.Auth;
using Basil.Domain.Users;

namespace Basil.Application.Auth.Persistence;

/// <summary>Records the history of logins.</summary>
/// <remarks>
///     The history is kept for audit; staff read it back, and each read is itself logged to the moderation log by the
///     caller.
/// </remarks>
public interface ILoginRepository
{
	/// <summary>Records a login.</summary>
	/// <param name="login">The login to record.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task RecordAsync(Login login, CancellationToken cancellationToken = default);

	/// <summary>Lists the recorded logins of a user, newest first.</summary>
	/// <param name="user">The user whose logins to list.</param>
	/// <param name="page">Which part of the listing to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>One part of the user's login history, newest first, with how many logins it has in total.</returns>
	ValueTask<Page<LoginEntry>> ListAsync(UserRecord user, PageRequest page,
		CancellationToken cancellationToken = default);
}
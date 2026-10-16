using Basil.Domain.Auth;

namespace Basil.Application.Storage.Contracts.Auth;

/// <summary>Records the history of logins.</summary>
/// <remarks>The history is kept for audit; no server operation reads it back.</remarks>
public interface ILoginRepository
{
	/// <summary>Records a login.</summary>
	/// <param name="login">The login to record.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task RecordAsync(Login login, CancellationToken cancellationToken = default);
}
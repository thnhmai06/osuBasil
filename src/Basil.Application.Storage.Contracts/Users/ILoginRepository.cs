using Basil.Application.Storage.Contracts.Common;
using Basil.Domain.Auth;

namespace Basil.Application.Storage.Contracts.Users;

/// <summary>Stores the history of logins.</summary>
public interface ILoginRepository
{
	/// <summary>Records a login.</summary>
	/// <param name="login">The login to record.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task CreateAsync(Login login, CancellationToken cancellationToken = default);

	/// <summary>Lists the logins a query includes, newest first.</summary>
	/// <param name="query">Which logins to include.</param>
	/// <param name="page">Which part of the listing to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>A page of logins.</returns>
	Task<Page<Login>> ListAsync(LoginQuery query, PageRequest page, CancellationToken cancellationToken = default);
}
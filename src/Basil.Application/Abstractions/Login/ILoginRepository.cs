namespace Basil.Application.Abstractions.Login;

/// <summary>
///     Records in-game login events in the IngameLogins table.
/// </summary>
public interface ILoginRepository
{
	/// <summary>
	///     Records a login event for a user and returns it as persisted.
	/// </summary>
	/// <param name="userId">The id of the user who logged in.</param>
	/// <param name="ip">The IP address the login came from.</param>
	/// <param name="osuVersion">The osu! version of the connecting client, as a date.</param>
	/// <param name="osuStream">The osu! release stream of the connecting client.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The newly created login event with its id and timestamp.</returns>
	Task<Domain.Login.Login> CreateAsync(int userId, string ip, DateOnly osuVersion, string osuStream,
		CancellationToken cancellationToken = default);
}
namespace Basil.Application.Abstractions.Users;

/// <summary>
///     A single in-game login event, as stored in the IngameLogins table.
/// </summary>
/// <param name="Id">The unique identifier of the login event.</param>
/// <param name="UserId">The id of the user who logged in.</param>
/// <param name="Ip">The IP address the login came from.</param>
/// <param name="OsuVer">The osu! version of the connecting client, as a date.</param>
/// <param name="OsuStream">The osu! release stream of the connecting client, for example stable or beta.</param>
/// <param name="LoggedInAt">The time the login occurred, in UTC.</param>
public sealed record IngameLogin(int Id, int UserId, string Ip, DateOnly OsuVer, string OsuStream, DateTime LoggedInAt);

/// <summary>
///     Records in-game login events in the IngameLogins table.
/// </summary>
public interface IIngameLoginRepository
{
	/// <summary>
	///     Records a login event for a user and returns it as persisted.
	/// </summary>
	/// <param name="userId">The id of the user who logged in.</param>
	/// <param name="ip">The IP address the login came from.</param>
	/// <param name="osuVer">The osu! version of the connecting client, as a date.</param>
	/// <param name="osuStream">The osu! release stream of the connecting client.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The newly created login event with its id and timestamp.</returns>
	Task<IngameLogin> CreateAsync(int userId, string ip, DateOnly osuVer, string osuStream,
		CancellationToken cancellationToken = default);
}
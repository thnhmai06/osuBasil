namespace Basil.Domain.Login;

/// <summary>
///     A single in-game login event, as stored in the IngameLogins table.
/// </summary>
/// <param name="Id">The unique identifier of the login event.</param>
/// <param name="UserId">The id of the user who logged in.</param>
/// <param name="Ip">The IP address the login came from.</param>
/// <param name="OsuVersion">The osu! version of the connecting client, as a date.</param>
/// <param name="OsuStream">The osu! release stream of the connecting client, for example stable or beta.</param>
/// <param name="LoggedInAt">The time the login occurred, in UTC.</param>
public sealed record Login(int Id, int UserId, string Ip, DateOnly OsuVersion, string OsuStream, DateTime LoggedInAt);
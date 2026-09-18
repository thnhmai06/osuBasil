using Basil.Domain.Client;
using Basil.Domain.Users;

namespace Basil.Domain.Auth;

/// <summary>
///     A single in-game login event, as stored in the IngameLogins table.
/// </summary>
/// <param name="Id">The unique identifier of the login event.</param>
/// <param name="Ip">The IP address the login came from.</param>
/// <param name="OccurredAt">The time the login occurred, in UTC.</param>
public sealed record LoginEvent(
	int Id,
	User User,
	string Ip,
	ClientVersion ClientVersion,
	DateTimeOffset OccurredAt);
using System.Net;
using Basil.Domain.Client;
using Basil.Domain.Users;

namespace Basil.Domain.Auth;

/// <summary>
///     A single in-game login.
/// </summary>
/// <param name="User">The user who logged in.</param>
/// <param name="Ip">The IP address the login came from.</param>
/// <param name="Version">The client version reported at login.</param>
/// <param name="Fingerprint">The client fingerprint reported at login.</param>
/// <param name="OccurredAt">The date and time when the login occurred.</param>
public sealed record Login(
	User User,
	IPAddress Ip,
	ClientVersion Version,
	ClientFingerprint Fingerprint,
	DateTimeOffset OccurredAt);
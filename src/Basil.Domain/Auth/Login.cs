using System.Net;
using Basil.Domain.Client;
using Basil.Domain.Users;

namespace Basil.Domain.Auth;

/// <summary>
///     A single in-game login.
/// </summary>
public sealed record Login(
	User User,
	IPAddress Ip,
	ClientVersion Version,
	ClientFingerprint Fingerprint,
	DateTimeOffset OccurredAt);
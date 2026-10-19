using System.Net;
using Basil.Domain.Users;

namespace Basil.Domain.Auth;

/// <summary>A recorded login of a user.</summary>
/// <param name="User">The user who logged in.</param>
/// <param name="Ip">The address the user logged in from.</param>
/// <param name="Timestamp">The moment the login happened.</param>
public sealed record LoginEntry(UserRecord User, IPAddress Ip, DateTimeOffset Timestamp);

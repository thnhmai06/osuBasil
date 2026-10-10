using System.Net;
using Basil.Domain.Client;
using Basil.Domain.Users;

namespace Basil.Domain.Auth;

/// <summary>A single login of a user; two logins are never equal, even with identical data.</summary>
public sealed class Login
{
	/// <summary>Gets the user who logged in.</summary>
	public required User User { get; init; }

	/// <summary>Gets the IP address the login came from.</summary>
	public required IPAddress Ip { get; init; }

	/// <summary>Gets the osu! client that logged in, or <see langword="null" /> for a login without an osu! client.</summary>
	public ClientInfo? Client { get; init; }

	/// <summary>Gets the date and time of the login.</summary>
	public required DateTimeOffset Timestamp { get; init; }
}
using Basil.Domain.Login;
using Basil.Domain.Users;

// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Application.Services.Users;

/// <summary>
///     The wire shape of a user in the <c>GET /users</c> and
///     <c>GET /users/{idOrName}</c> API responses.
/// </summary>
/// <remarks>
///     Carries the same fields as the domain <see cref="User" /> record, kept separate so the API
///     layer never serializes a domain type directly.
/// </remarks>
public sealed record UserView(
	int Id,
	string Name,
	Country Country,
	UserPrivileges Privilege,
	DateTimeOffset SilenceEnd);

/// <summary>
///     Maps domain <see cref="User" /> records to their API-facing <see cref="UserView" /> shape.
/// </summary>
public static class UserViewMapper
{
	/// <summary>
	///     Converts a domain <see cref="User" /> to its API-facing <see cref="UserView" />.
	/// </summary>
	/// <param name="user">The user to convert.</param>
	/// <returns>The corresponding <see cref="UserView" />.</returns>
	public static UserView ToView(this User user)
	{
		return new UserView(user.Id, user.Name, user.Country, user.Privilege, user.SilenceEnd);
	}
}
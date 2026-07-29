using Basil.Domain.Login;
using Basil.Domain.Users;

namespace Basil.Application.Services.Users;

/// <summary>
///     Wire shape for `GET /users` (list) and `GET /users/{idOrName}` — same fields as the domain <see cref="User" />
///     record, kept separate so the API layer doesn't serialize a domain type directly.
/// </summary>
public sealed record UserView(
	int Id,
	string Name,
	Country Country,
	UserPrivileges Privilege,
	DateTimeOffset SilenceEnd);

public static class UserViewMapper
{
	public static UserView ToView(this User user)
	{
		return new UserView(user.Id, user.Name, user.Country, user.Privilege, user.SilenceEnd);
	}
}
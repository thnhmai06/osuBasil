using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to create an account.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Data">The profile of the new account.</param>
/// <param name="PasswordHash">The hash of the password of the new account.</param>
/// <param name="Privileges">The privileges to grant, or null for the server's default.</param>
public sealed record CreateAccount(Connection Actor, UserData Data, Md5 PasswordHash, UserPrivileges? Privileges)
	: IRequest<(UserRecord? User, Result<UserOutcome> Result)>;
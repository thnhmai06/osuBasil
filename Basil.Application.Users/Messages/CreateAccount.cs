using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record CreateAccount(Session Actor, UserData Data, Md5Hash PasswordHash, UserPrivileges? Privileges)
	: IRequest<(UserRecord? User, UserActionResult Result)>;
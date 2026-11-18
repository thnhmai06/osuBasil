using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;

using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record CreateAccount(Connection Actor, UserData Data, Md5 PasswordHash, UserPrivileges? Privileges)
	: IRequest<(UserRecord? User, UserOutcome Result)>;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record ChangeUserPassword(
	Session Actor,
	UserReference Target,
	Md5Hash NewPasswordHash,
	Md5Hash? CurrentPasswordHash,
	bool IncludeCurrent) : IRequest<UserActionResult>;
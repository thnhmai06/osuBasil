using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Cryptography;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record ChangeUserPassword(
	Connection Actor,
	UserReference Target,
	Md5 NewPasswordHash,
	Md5? CurrentPasswordHash,
	bool IncludeCurrent) : IRequest<UserOutcome>;
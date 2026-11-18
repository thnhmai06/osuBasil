using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetUserCountry(Connection Actor, UserReference Target, Country Country)
	: IRequest<UserOutcome>;
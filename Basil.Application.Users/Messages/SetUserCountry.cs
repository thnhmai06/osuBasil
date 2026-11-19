using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetUserCountry(Session Actor, UserReference Target, Country Country)
	: IRequest<UserActionResult>;
using Basil.Application.Shared.Actors;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetUserCountry(IActor Actor, int TargetId, Country Country)
	: IRequest<UserActionResult>;
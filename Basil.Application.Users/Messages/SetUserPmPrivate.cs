using Basil.Application.Shared.Actors;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetUserPmPrivate(IActor Actor, int TargetId, bool PmPrivate) : IRequest<UserActionResult>;
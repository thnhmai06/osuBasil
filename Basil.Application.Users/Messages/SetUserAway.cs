using Basil.Application.Shared.Actors;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetUserAway(IActor Actor, int TargetId, string? Message) : IRequest<UserActionResult>;
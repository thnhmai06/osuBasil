using Basil.Application.Shared.Actors;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetUserAvatar(IActor Actor, int TargetId, Stream Image) : IRequest<UserActionResult>;
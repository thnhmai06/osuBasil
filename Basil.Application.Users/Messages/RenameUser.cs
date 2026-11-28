using Basil.Application.Shared.Actors;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record RenameUser(IActor Actor, int TargetId, string Name) : IRequest<UserActionResult>;
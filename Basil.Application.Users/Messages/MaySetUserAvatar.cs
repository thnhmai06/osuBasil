using Basil.Application.Shared.Actors;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks whether an actor may set a user's avatar, so a refused upload is not read at all.</summary>
public sealed record MaySetUserAvatar(IActor Actor, int TargetId) : IRequest<UserActionResult>;

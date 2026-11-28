using Basil.Application.Shared.Actors;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record RemoveFriend(IActor Actor, int Id, int OtherId) : IRequest<UserActionResult>;
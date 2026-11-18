using Basil.Application.Users.Objects;
using Basil.Domain.Game.Mechanics;
using Basil.Domain.Users;
using Mediator;
using User = Basil.Application.Users.Objects.User;

namespace Basil.Application.Scores.Messages;

public sealed record GetUserStats(User User, GameMode Mode) : IRequest<UserStats>;
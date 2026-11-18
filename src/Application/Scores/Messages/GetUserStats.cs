using Basil.Application.Users.Objects;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Scores.Messages;

public sealed record GetUserStats(User User, GameMode Mode) : IRequest<UserStats>;
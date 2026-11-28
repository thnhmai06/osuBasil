using Basil.Domain.Game;
using Basil.Domain.Users;
using Mediator;
using User = Basil.Application.Users.Objects.User;

namespace Basil.Application.Scores.Messages;

public sealed record GetUserStats(User User, GameMode Mode) : IRequest<UserStats>;

/// <summary>Gets the statistics of each user in the given mode; the answer has one entry per request, in order.</summary>
public sealed record GetUsersStats(IReadOnlyList<(User User, GameMode Mode)> Players)
	: IRequest<IReadOnlyList<UserStats>>;
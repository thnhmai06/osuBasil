using Basil.Application.Users.Objects;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Scores.Messages;

/// <summary>Asks Scores for the statistics of an online user in one game mode.</summary>
/// <param name="User">The user whose statistics are read.</param>
/// <param name="Mode">The game mode.</param>
/// <remarks>The statistics of a user are public; a user with no plays has zeroed statistics.</remarks>
public sealed record GetUserStats(User User, GameMode Mode) : IRequest<UserStats>;
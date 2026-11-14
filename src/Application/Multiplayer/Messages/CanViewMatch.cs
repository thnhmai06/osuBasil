using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks Multiplayer whether a connection's user may view the data that belongs to a match.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="MatchId">The id of the match.</param>
/// <remarks>The answer is <see langword="true" /> when the match exists and the user may view it.</remarks>
public sealed record CanViewMatch(Connection Actor, int MatchId) : IRequest<bool>;
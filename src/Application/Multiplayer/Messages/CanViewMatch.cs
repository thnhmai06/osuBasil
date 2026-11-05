using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks Multiplayer whether a connection's user may view the data that belongs to a match.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Match">The match.</param>
/// <remarks>The answer is <see langword="true" /> when the user may view the match.</remarks>
public sealed record CanViewMatch(Connection Actor, MatchRecord Match) : IRequest<bool>;
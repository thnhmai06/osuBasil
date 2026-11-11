using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to make a connection spectate an osu! client.</summary>
/// <param name="Actor">The connection that spectates.</param>
/// <param name="Host">The osu! client to spectate.</param>
public sealed record SpectateUser(Connection Actor, StableConnection Host) : IRequest<ConnectionOutcome>;
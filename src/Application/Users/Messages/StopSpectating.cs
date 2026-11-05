using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to stop a connection spectating an osu! client.</summary>
/// <param name="Actor">The connection that stops spectating.</param>
/// <param name="Host">The osu! client it spectates.</param>
/// <remarks>The answer is <see langword="true" /> when the connection stopped spectating; asking again does nothing.</remarks>
public sealed record StopSpectating(Connection Actor, StableConnection Host) : IRequest<bool>;
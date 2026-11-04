using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Client;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Hands the frames an osu! client sent to the HTTP API connections spectating it.</summary>
/// <param name="Actor">The osu! client that played the frames.</param>
/// <param name="Bundle">The decoded frames.</param>
/// <remarks>The answer is <see langword="true" /> when the frames were published.</remarks>
public sealed record PublishSpectatorFrames(StableConnection Actor, SpectatorBundle Bundle) : IRequest<bool>;

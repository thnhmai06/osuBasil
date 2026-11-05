using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Records the presence status an osu! client reported.</summary>
/// <param name="Actor">The osu! client that reported.</param>
/// <param name="Status">The reported status.</param>
/// <remarks>The answer is <see langword="true" /> when the status changed.</remarks>
public sealed record SetPlayerStatus(StableConnection Actor, PlayerStatus Status) : IRequest<bool>;
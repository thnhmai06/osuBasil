using Basil.Application.Sessions.Objects;
using Basil.Domain.Client;
using Mediator;

namespace Basil.Application.Anticheat.Messages;

/// <summary>Reports the detection flags an osu! client sent about a player; the answer is whether they show a sign of cheating.</summary>
/// <param name="Player">The session of the player.</param>
/// <param name="Flags">The flags the client reported.</param>
public sealed record ReportClientFlags(StableSession Player, ClientFlags Flags) : IRequest<bool>;

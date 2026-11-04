using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Tells Users that a connection could not spectate an osu! client, for example because it lacks the beatmap.</summary>
/// <param name="Actor">The connection that failed to spectate.</param>
/// <param name="Host">The osu! client it spectates.</param>
/// <remarks>The answer is <see langword="true" /> when the connection is spectating the client.</remarks>
public sealed record ReportSpectatingFailed(Connection Actor, StableConnection Host) : IRequest<bool>;

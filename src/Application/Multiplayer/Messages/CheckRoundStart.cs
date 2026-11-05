using Basil.Application.Common;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks whether a connection may start rounds in a room, without starting one.</summary>
/// <param name="Actor">The connection that acts.</param>
/// <param name="Room">The room it would act on.</param>
/// <remarks>
///     The answer is Ok when the connection may start rounds, otherwise the refusal that <see cref="StartRound" />
///     gives.
/// </remarks>
public sealed record CheckRoundStart(Connection Actor, Room Room) : IRequest<Result<RoomOutcome>>;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

public sealed record CanViewMatch(Connection Actor, int MatchId) : IRequest<bool>;
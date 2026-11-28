using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

public sealed record CanViewMatch(Session Actor, int MatchId) : IRequest<bool>;
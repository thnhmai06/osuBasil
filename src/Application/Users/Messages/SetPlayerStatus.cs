using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetPlayerStatus(StableConnection Actor, PlayerStatus Status) : IRequest<bool>;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetAwayMessage(Connection Actor, string? Message) : IRequest<bool>;
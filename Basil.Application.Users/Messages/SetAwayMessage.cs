using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetAwayMessage(Session Actor, string? Message) : IRequest<bool>;
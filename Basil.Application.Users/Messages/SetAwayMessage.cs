using Basil.Application.Shared.Actors;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetAwayMessage(IActor Actor, string? Message) : IRequest<bool>;
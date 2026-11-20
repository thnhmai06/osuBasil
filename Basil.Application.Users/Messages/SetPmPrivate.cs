using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetPmPrivate(Session Actor, bool PmPrivate) : IRequest<bool>;
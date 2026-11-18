using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetPmPrivate(Connection Actor, bool PmPrivate) : IRequest<bool>;
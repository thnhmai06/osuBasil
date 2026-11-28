using Basil.Application.Shared.Actors;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetPmPrivate(IActor Actor, bool PmPrivate) : IRequest<bool>;
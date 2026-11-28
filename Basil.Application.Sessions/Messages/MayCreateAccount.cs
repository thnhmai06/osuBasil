using Basil.Application.Sessions.Objects;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Sessions.Messages;

/// <summary>Asks whether a session may create accounts, so a refused request is not read at all.</summary>
public sealed record MayCreateAccount(Session Actor) : IRequest<UserActionResult>;

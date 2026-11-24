using Basil.Application.Sessions.Objects;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Sessions.Messages;

/// <summary>Asks whether a session may change the password of an account, so a refused change is not read at all.</summary>
public sealed record MayChangeUserPassword(Session Actor, int TargetId) : IRequest<UserActionResult>;

using Basil.Application.Sessions.Persistence;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Sessions.Messages;

/// <summary>Creates an account for a person who signs up, or says why it cannot.</summary>
public sealed record RegisterAccount(RegisterAttempt Attempt) : IRequest<(UserRecord? User, RegistrationFailure? Failure)>;

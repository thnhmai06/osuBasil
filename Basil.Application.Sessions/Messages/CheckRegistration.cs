using Basil.Application.Sessions.Persistence;
using Mediator;

namespace Basil.Application.Sessions.Messages;

/// <summary>Says why an account could not be registered with the attempt, or null when it could, without creating it.</summary>
public sealed record CheckRegistration(RegisterAttempt Attempt) : IRequest<RegistrationFailure?>;

using Basil.Application.Sessions.Objects;
using Basil.Domain.Sessions;
using Session = Basil.Application.Sessions.Objects.Session;
using Mediator;

namespace Basil.Application.Sessions.Messages;

/// <summary>Ends a session for the given reason.</summary>
public sealed record CloseSession(Session Session, SessionEndReason Reason) : IRequest;

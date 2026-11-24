using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Sessions.Messages;

/// <summary>Records that a session was heard from; a session silent past its expiry is closed instead.</summary>
public sealed record MarkSessionActive(Session Session) : IRequest;

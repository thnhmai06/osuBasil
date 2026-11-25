using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Sessions.Messages;

/// <summary>Brings back the osu! session that holds a token and records that it was heard from; the answer is <see langword="null"/> when the token is unknown or the session has expired.</summary>
public sealed record ResumeStableSession(string Token) : IRequest<Session?>;

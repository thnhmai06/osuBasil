using Basil.Application.Sessions.Objects;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Sessions.Messages;

/// <summary>Brings back the API session that holds a token, when its account may still use it.</summary>
public sealed record ResumeSession(string Token, Func<UserRecord, bool>? Accepts = null) : IRequest<LoginResult>;

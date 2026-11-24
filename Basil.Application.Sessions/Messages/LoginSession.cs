using System.Net;
using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Persistence;
using Basil.Domain.Client;
using Basil.Domain.Sessions;
using Mediator;

namespace Basil.Application.Sessions.Messages;

/// <summary>Signs an account in and opens a session of the given type for it.</summary>
public sealed record LoginSession(
	LoginAttempt Attempt,
	SessionType Type,
	IPAddress Ip,
	ClientBuild? Client,
	int UtcOffset) : IRequest<LoginResult>;

using Basil.Domain.Users;

namespace Basil.Domain.Auth;

/// <summary>One client session as it exists in the stored history, whether or not the server is running.</summary>
/// <param name="Id">The public identifier of the session.</param>
/// <param name="User">The user who logged in.</param>
/// <param name="Type">The kind of client that opened the session.</param>
/// <param name="StartedAt">The moment the session started.</param>
/// <param name="Value">The device, credential and end of the session.</param>
public sealed record SessionRecord(
	Guid Id,
	UserRecord User,
	SessionType Type,
	DateTimeOffset StartedAt,
	SessionData Value);

using Basil.Domain.Shared;
using Basil.Domain.Users;

namespace Basil.Domain.Moderation;

public sealed record ModerationLog
{
	public required long Id { get; init; }

	public required ModerationData Value { get; init; }
}

public sealed record ModerationData
{
	public required ModerationAction Action
	{
		get;
		init => field = value.ThrowIfUndefined();
	}

	public required UserRecord Actor { get; init; }

	public required UserRecord Target { get; init; }

	public string? Reason
	{
		get;
		init => field = value is { Length: > 2048 }
			? throw new ArgumentException("A moderation log reason cannot be longer than 2048 characters.",
				nameof(value))
			: value;
	}

	public required DateTimeOffset Timestamp { get; init; }
}

public enum ModerationAction : byte
{
	Silence = 0,

	Unsilence = 1,

	Note = 2,

	ViewLogins = 3,

	CloseSessions = 5,

	ChangePassword = 6
}
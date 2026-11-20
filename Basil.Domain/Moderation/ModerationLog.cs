using Basil.Domain.Users;
using Basil.Shared.Utilities;

namespace Basil.Domain.Moderation;

/// <summary>One entry of the moderation history.</summary>
public sealed record ModerationLog
{
	/// <summary>The identifier the store assigned to the entry.</summary>
	public required long Id { get; init; }

	/// <summary>What the entry records.</summary>
	public required ModerationLogData Value { get; init; }
}

/// <summary>What a moderation log entry records: who did what to whom, when and why.</summary>
public sealed record ModerationLogData
{
	/// <summary>What the actor did.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is not a defined action.</exception>
	public required ModerationAction Action
	{
		get;
		init => field = value.ThrowIfInvalid();
	}

	/// <summary>The user who did it.</summary>
	public required UserRecord Actor { get; init; }

	/// <summary>The user it was done to.</summary>
	public required UserRecord Target { get; init; }

	/// <summary>The reason the actor gave, or <see langword="null" /> when none.</summary>
	/// <exception cref="ArgumentException">The value is longer than 2048 characters.</exception>
	public string? Reason
	{
		get;
		init => field = value is { Length: > 2048 }
			? throw new ArgumentException("A moderation log reason cannot be longer than 2048 characters.",
				nameof(value))
			: value;
	}

	/// <summary>The moment the action happened.</summary>
	public required DateTimeOffset Timestamp { get; init; }
}

/// <summary>What a moderation log entry records the actor doing to the target.</summary>
public enum ModerationAction : byte
{
	/// <summary>Imposed a silence.</summary>
	Silence = 0,

	/// <summary>Lifted a silence.</summary>
	Unsilence = 1,

	/// <summary>Added a note about the target.</summary>
	Note = 2,

	/// <summary>Viewed the target's sessions and login history.</summary>
	ViewLogins = 3,

	/// <summary>Ended the target's sessions.</summary>
	CloseSessions = 5,

	/// <summary>Changed the target's password.</summary>
	ChangePassword = 6
}
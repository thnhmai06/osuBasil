namespace Basil.Domain.Users;

/// <summary>
///     An entry of the moderation log, identified by its id.
/// </summary>
public sealed class ModerationLogEntry
{
	/// <summary>Gets the unique identifier of the entry, assigned when it is stored.</summary>
	public required long Id { get; init; }

	/// <summary>Gets the moderation log data this identity wraps.</summary>
	public required ModerationLogData Value { get; init; }
}

/// <summary>A recorded moderation action, separate from its persistent identity.</summary>
public sealed class ModerationLogData
{
	/// <summary>Gets the kind of moderation action that was recorded.</summary>
	public required ModerationAction Action { get; init; }

	/// <summary>Gets the user who performed the action.</summary>
	public required UserRecord By { get; init; }

	/// <summary>Gets the user the action was aimed at.</summary>
	public required UserRecord Target { get; init; }

	/// <summary>Gets or sets the reason recorded with the action, if any.</summary>
	/// <exception cref="ArgumentException">The reason is longer than 2048 characters.</exception>
	public string? Reason
	{
		get;
		set => field = value is { Length: > 2048 }
			? throw new ArgumentException("A moderation log reason cannot be longer than 2048 characters.",
				nameof(value))
			: value;
	}

	/// <summary>Gets the moment the action was recorded.</summary>
	public required DateTimeOffset Timestamp { get; init; }
}

/// <summary>The kinds of action the moderation log records.</summary>
public enum ModerationAction : byte
{
	/// <summary>A user's permissions were suspended by a restriction.</summary>
	Restrict = 0,

	/// <summary>A restriction of a user's permissions was lifted.</summary>
	Lift = 1,

	/// <summary>A note was added to the moderation history of a user.</summary>
	Note = 2,

	/// <summary>The login history of a user was read.</summary>
	ViewLogins = 3
}

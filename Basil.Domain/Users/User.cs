using Basil.Domain.Game;

namespace Basil.Domain.Users;

/// <summary>A user of the system: the stored account together with the user's statistics, current status and messaging preferences.</summary>
/// <param name="record">The stored account.</param>
/// <param name="stats">The user's statistics, one entry per game mode.</param>
public sealed class User(UserRecord record, IReadOnlyDictionary<GameMode, UserStats> stats)
{
	/// <summary>The stored account of the user.</summary>
	public UserRecord Record { get; } = record;

	/// <summary>The user's statistics, one entry per game mode.</summary>
	public Dictionary<GameMode, UserStats> Stats { get; init; } = stats.ToDictionary();

	/// <summary>What the user is doing right now.</summary>
	public UserStatus Status { get; init; } = new();

	/// <summary>The message shown to people who write to the user while the user is away, or <see langword="null" /> when the user is not away; blank text counts as no message.</summary>
	public string? AwayMessage
	{
		get;
		set => field = string.IsNullOrWhiteSpace(value) ? null : value;
	} = null;

	/// <summary>Whether the user accepts private messages only from friends.</summary>
	public bool PmPrivate { get; set; } = false;

	/// <summary>Which users' presence the user receives: nobody, everybody or only friends.</summary>
	public PresenceVisibility PresenceVisibility { get; set; } = PresenceVisibility.All;
}
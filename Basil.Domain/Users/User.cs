using Basil.Domain.Mechanics;

namespace Basil.Domain.Users;

public sealed class User(UserRecord record, IReadOnlyDictionary<GameMode, UserStats> stats)
{
	public UserRecord Record { get; } = record;

	public Dictionary<GameMode, UserStats> Stats { get; init; } = stats.ToDictionary();

	public UserStatus Status { get; init; } = new();

	public string? AwayMessage
	{
		get;
		set => field = string.IsNullOrWhiteSpace(value) ? null : value;
	} = null;

	public bool PmPrivate { get; set; } = false;

	public PresenceVisibility PresenceVisibility { get; set; } = PresenceVisibility.All;
}
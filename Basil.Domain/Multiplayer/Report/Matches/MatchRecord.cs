using Basil.Domain.Multiplayer.Room;
using Basil.Domain.Users;
using Basil.Shared.Utilities;

namespace Basil.Domain.Multiplayer.Report.Matches;

public sealed class MatchRecord : IEquatable<MatchRecord>
{
	public required int Id
	{
		get;
		init
		{
			if (value < 1)
				throw new ArgumentOutOfRangeException(nameof(value), value, "Match ids start at 1.");
			field = value;
		}
	}

	public required MatchData Value { get; init; }

	public bool Equals(MatchRecord? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	public override bool Equals(object? obj)
	{
		return obj is MatchRecord other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Id.GetHashCode();
	}
}

public sealed class MatchData
{
	public required string Name
	{
		get;
		set => field = IsValidName(value)
			? value
			: throw new InvalidRoomSettingsException();
	}

	public required UserRecord Creator { get; init; }

	public required DateTimeOffset StartedAt { get; init; }

	public required DateTimeOffset? EndedAt { get; set; }

	public bool IsTournament { get; init; } = false;

	public bool IsPrivate { get; set; } = false;

	public static bool IsValidName(string name)
	{
		return !string.IsNullOrWhiteSpace(name) && !name.HasNul();
	}
}
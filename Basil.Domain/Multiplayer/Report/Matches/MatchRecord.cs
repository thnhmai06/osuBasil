using Basil.Domain.Multiplayer.Room;
using Basil.Domain.Users;
using Basil.Shared.Utilities;

namespace Basil.Domain.Multiplayer.Report.Matches;

/// <summary>A match identified by its id; two records are equal when their ids are.</summary>
public sealed class MatchRecord : IEquatable<MatchRecord>
{
	/// <summary>Gets the identifier of the match.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The id is below 1.</exception>
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

	/// <summary>Gets the data of the match.</summary>
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

/// <summary>The data of a match: its name, creator, time span and visibility.</summary>
public sealed class MatchData
{
	/// <summary>The most characters a match name keeps; a longer name is cut to this length.</summary>
	public const int MaxNameLength = 50;

	/// <summary>Gets or sets the name of the match; a name longer than <see cref="MaxNameLength"/> is cut.</summary>
	/// <exception cref="InvalidRoomSettingsException">The name is blank or contains a NUL character.</exception>
	public required string Name
	{
		get;
		set => field = IsValidName(value)
			? (value.Length > MaxNameLength ? value[..MaxNameLength] : value)
			: throw new InvalidRoomSettingsException();
	}

	/// <summary>Gets the user who created the match.</summary>
	public required UserRecord Creator { get; init; }

	/// <summary>Gets when the match started.</summary>
	public required DateTimeOffset StartedAt { get; init; }

	/// <summary>Gets or sets when the match ended, or <see langword="null"/> while it is open.</summary>
	public required DateTimeOffset? EndedAt { get; set; }

	/// <summary>Gets whether the match is part of a tournament.</summary>
	public bool IsTournament { get; init; } = false;

	/// <summary>Gets or sets whether the match is private.</summary>
	public bool IsPrivate { get; set; } = false;

	/// <summary>Gets whether a text can name a match: it is not blank and has no NUL character.</summary>
	/// <param name="name">The text to check.</param>
	public static bool IsValidName(string name)
	{
		return !string.IsNullOrWhiteSpace(name) && !name.HasNul();
	}
}
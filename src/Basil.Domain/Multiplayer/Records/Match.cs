namespace Basil.Domain.Multiplayer.Records;

/// <summary>
///     A match record as read back for report and management purposes.
/// </summary>
public sealed class Match : IEquatable<Match>
{
	public required int Id { get; init; }
	public required string Name { get; set; }
	public required DateTimeOffset CreatedAt { get; init; }
	public required DateTimeOffset? EndedAt { get; set; }

	/// <summary>
	///     Gets or sets a value that indicates whether the match is visible to public.
	/// </summary>
	public bool IsVisible { get; set; } = true;

	public bool Equals(Match? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	public override bool Equals(object? obj)
	{
		return obj is Match other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Id;
	}
}
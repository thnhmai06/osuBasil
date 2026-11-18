using Basil.Shared.Utilities;

namespace Basil.Domain.Content;

public sealed class MenuBanner : IEquatable<MenuBanner>
{
	public required ContentReference Image
	{
		get;
		init => field = value.Value is null
			? throw new ArgumentException("The image must refer to an asset.", nameof(value))
			: value;
	}

	public required Uri Url
	{
		get;
		set => field = value.ThrowIfHttp();
	}

	public required DateTimeOffset? StartsAt
	{
		get;
		set
		{
			if (value > EndsAt)
				throw new ArgumentOutOfRangeException(nameof(value), value, "A banner cannot start after it ends.");
			field = value;
		}
	}

	public required DateTimeOffset? EndsAt
	{
		get;
		set
		{
			if (value < StartsAt)
				throw new ArgumentOutOfRangeException(nameof(value), value, "A banner cannot end before it starts.");
			field = value;
		}
	}

	public required DateTimeOffset CreatedAt { get; init; }

	public bool Equals(MenuBanner? other)
	{
		if (other is null) return false;
		return Image == other.Image;
	}

	public bool IsCurrent(DateTimeOffset now)
	{
		return (StartsAt is null || StartsAt <= now) && (EndsAt is null || now < EndsAt);
	}

	public override bool Equals(object? obj)
	{
		return obj is MenuBanner other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Image.GetHashCode();
	}
}
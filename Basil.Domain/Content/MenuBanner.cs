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
		set => field = value.IsHttp() ? value : throw new InvalidAddressException();
	}

	public required DateTimeOffset? StartsAt
	{
		get;
		set
		{
			if (!IsValidWindow(value, EndsAt))
				throw new InvalidBannerWindowException(value, EndsAt);
			field = value;
		}
	}

	public required DateTimeOffset? EndsAt
	{
		get;
		set
		{
			if (!IsValidWindow(StartsAt, value))
				throw new InvalidBannerWindowException(StartsAt, value);
			field = value;
		}
	}

	public required DateTimeOffset CreatedAt { get; init; }

	public bool IsCurrent(DateTimeOffset now)
	{
		return (StartsAt is null || StartsAt <= now) && (EndsAt is null || now < EndsAt);
	}

	private static bool IsValidWindow(DateTimeOffset? startsAt, DateTimeOffset? endsAt)
	{
		return !(startsAt > endsAt);
	}

	public bool Equals(MenuBanner? other)
	{
		if (other is null) return false;
		return Image == other.Image;
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
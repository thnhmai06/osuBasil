namespace Basil.Domain.Authentication;

public readonly record struct SessionToken(Guid Id) : IFormattable
{
	public static readonly SessionToken Empty = new (Guid.Empty);

	public SessionToken() : this(Guid.NewGuid())
	{
	}

	public static implicit operator SessionToken(Guid other)
	{
		return new SessionToken(other);
	}

	public string ToString(string? format, IFormatProvider? formatProvider)
	{
		return Id.ToString("D");
	}
}
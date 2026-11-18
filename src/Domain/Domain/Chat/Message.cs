using Basil.Domain.Users;

namespace Basil.Domain.Chat;

public sealed record Message
{
	public required UserRecord Author { get; init; }

	public required string Content
	{
		get;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			if (value.Length <= 0 || value.Any(c => char.IsControl(c) && c is not ('\t' or '\n' or '\r' or '\u0001')))
				throw new ArgumentException("Message content cannot be empty or hold control characters.",
					nameof(value));
			field = value;
		}
	}

	public required DateTimeOffset Timestamp { get; init; }

	public bool IsNotice { get; init; } = false;

	public static bool IsValidContent(string text)
	{
		return text.Length > 0 && !text.Any(c => char.IsControl(c) && c is not ('\t' or '\n' or '\r' or '\u0001'));
	}
}
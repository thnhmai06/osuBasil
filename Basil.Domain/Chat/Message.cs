using Basil.Domain.Users;

namespace Basil.Domain.Chat;

/// <summary>A chat message and who wrote it.</summary>
public sealed record Message
{
	/// <summary>The user who wrote the message.</summary>
	public required UserRecord Author { get; init; }

	/// <summary>The text of the message: not empty and without control characters other than tab, line breaks and the action marker.</summary>
	/// <exception cref="EmptyMessageException">The value is empty.</exception>
	/// <exception cref="InvalidMessageException">The value holds a disallowed control character.</exception>
	public required string Content
	{
		get;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			if (value.Length == 0) throw new EmptyMessageException();
			if (!IsValidContent(value)) throw new InvalidMessageException();
			field = value;
		}
	}

	/// <summary>The moment the message was sent.</summary>
	public required DateTimeOffset Timestamp { get; init; }

	/// <summary>Whether the message is a notice rather than an ordinary message; false by default.</summary>
	public bool IsNotice { get; init; } = false;

	/// <summary>Checks whether a text can be the content of a message.</summary>
	/// <param name="text">The text to check.</param>
	/// <returns><see langword="true" /> when the text is not empty and holds no disallowed control character.</returns>
	public static bool IsValidContent(string text)
	{
		return text.Length > 0 && !text.Any(c => char.IsControl(c) && c is not ('\t' or '\n' or '\r' or '\u0001'));
	}
}
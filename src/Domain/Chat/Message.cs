using Basil.Domain.Users;

namespace Basil.Domain.Chat;

/// <summary>A message posted to a chat channel.</summary>
public sealed record Message
{
	/// <summary>Gets the user who posted the message.</summary>
	public required UserRecord Author { get; init; }

	/// <summary>Gets the text of the message.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException">The value is empty or holds a character that chat does not carry.</exception>
	/// <remarks>
	///     Whitespace is part of the text, so a message of only whitespace is allowed. Control characters are refused,
	///     except the tab, the line feeds and the character that wraps a <c>/me</c> action sent by the osu! client.
	/// </remarks>
	public required string Content
	{
		get;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			if (!IsValidContent(value))
				throw new ArgumentException("Message content cannot be empty or hold control characters.",
					nameof(value));
			field = value;
		}
	}

	/// <summary>Gets the date and time the message was posted.</summary>
	public required DateTimeOffset Timestamp { get; init; }

	/// <summary>Gets a value that indicates whether the message is a notice, which never triggers an automatic reply.</summary>
	public bool IsNotice { get; init; } = false;

	/// <summary>Determines whether a text can be the content of a message.</summary>
	/// <param name="text">The text to check.</param>
	/// <returns>
	///     <see langword="true" /> when the text is not empty and holds no control character other than the tab, the
	///     line feeds and the action wrapper (U+0001).
	/// </returns>
	public static bool IsValidContent(string text)
	{
		return text.Length > 0 && !text.Any(c => char.IsControl(c) && c is not ('\t' or '\n' or '\r' or '\u0001'));
	}
}
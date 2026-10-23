namespace Basil.Bot.Replies;

/// <summary>Turns a reply into the text BasilBot says, using the wording the replies file gives it.</summary>
internal interface IReplies
{
	/// <summary>Gets the text of a reply.</summary>
	/// <param name="reply">The reply to say.</param>
	/// <param name="arguments">
	///     The values of the reply's arguments, as an object whose properties are named like the arguments, or
	///     <see langword="null" /> for a reply without arguments.
	/// </param>
	/// <returns>The text, with each argument filled in.</returns>
	/// <exception cref="ArgumentException"><paramref name="arguments" /> lacks an argument the reply declares.</exception>
	string Text(Reply reply, object? arguments = null);
}

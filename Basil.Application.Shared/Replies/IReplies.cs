namespace Basil.Application.Shared.Replies;

/// <summary>The wording of the replies one surface (a chat bot, a protocol host) speaks, one text per member of its reply enum.</summary>
/// <typeparam name="TReply">The reply enum the surface owns; each member declares its default wording with <see cref="ReplyAttribute"/>.</typeparam>
public interface IReplies<TReply> where TReply : struct, Enum
{
	/// <summary>Gets the wording of a reply with its named arguments filled in.</summary>
	/// <param name="reply">The reply to word.</param>
	/// <param name="arguments">An object whose properties carry the arguments the reply declares, or <see langword="null"/> when it declares none.</param>
	/// <returns>The text to show.</returns>
	/// <exception cref="ArgumentException">An argument the reply declares is missing.</exception>
	string Text(TReply reply, object? arguments = null);
}

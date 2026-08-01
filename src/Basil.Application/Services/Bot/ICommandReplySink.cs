namespace Basil.Application.Services.Bot;

/// <summary>
///     Receives the reply text a chat command produces and routes it to the right destination.
/// </summary>
/// <remarks>
///     A sink is constructed by <see cref="Chat.ChatDispatchService" /> for each invocation from the
///     message's source, either a channel or a DM to the bot, so every command implementation sends
///     its own reply instead of returning text for the caller to route. The caller only ever sees a
///     success or failure boolean. <see cref="Reply" /> follows the source's normal routing rule,
///     while <see cref="ReplyDm" /> always goes to the sender's DM regardless of source, which is
///     how <c>!help</c> and <c>!mp help</c> reach the sender privately.
/// </remarks>
public interface ICommandReplySink
{
	/// <summary>Routes a command reply through the source's normal delivery path.</summary>
	/// <param name="text">The reply text to send.</param>
	void Reply(string text);

	/// <summary>Routes a command reply to the sender's DM regardless of the source.</summary>
	/// <param name="text">The reply text to send.</param>
	void ReplyDm(string text);
}
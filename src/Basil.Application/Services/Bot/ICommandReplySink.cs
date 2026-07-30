namespace Basil.Application.Services.Bot;

/// <summary>
///     Where a command's reply text goes — constructed by <see cref="Chat.ChatDispatchService" /> per
///     invocation from the message's source (channel vs bot DM), so every command implementation sends
///     its own reply instead of returning text for the caller to route (the caller only ever sees a
///     success/failure <c>bool</c>). <see cref="Reply" /> follows the source's normal routing rule;
///     <see cref="ReplyDm" /> always goes to the sender's DM regardless of source, for `!help`/`!mp help`.
/// </summary>
public interface ICommandReplySink
{
	void Reply(string text);

	void ReplyDm(string text);
}

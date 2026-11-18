namespace Basil.Application.Bot.Replies;

internal interface IReplies
{
	string Text(Reply reply, object? arguments = null);
}
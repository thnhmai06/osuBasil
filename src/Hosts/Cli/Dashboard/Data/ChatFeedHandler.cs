using Basil.Application.Chat.Runtime;
using Mediator;

namespace Basil.Host.Cli.Dashboard.Data;

/// <summary>Copies every posted chat message into the chat tab's feed, without waiting for anything.</summary>
internal sealed class ChatFeedHandler(ChatFeed feed) : INotificationHandler<ChannelMessagePosted>
{
	public ValueTask Handle(ChannelMessagePosted notification, CancellationToken cancellationToken)
	{
		var message = notification.Message;
		feed.Add(new ChatLine(message.Timestamp, notification.Channel.Name, message.Author.Value.Name, message.Content));
		return ValueTask.CompletedTask;
	}
}

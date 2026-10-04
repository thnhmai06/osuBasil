using System.Threading.Channels;
using Basil.Application.Contracts.Chat;
using Channel = System.Threading.Channels.Channel;

namespace Basil.Application.Services.Chat;

/// <summary>The one event stream of chat channels, shared by the channel service and its children.</summary>
internal sealed class ChannelEventStream
{
	private readonly Channel<ChannelEvent> _events = Channel.CreateUnbounded<ChannelEvent>();

	/// <summary>Gets the reader of the events emitted so far.</summary>
	internal ChannelReader<ChannelEvent> Reader => _events.Reader;

	/// <summary>Emits an event to the stream.</summary>
	/// <param name="event">The event to emit.</param>
	internal void Emit(ChannelEvent @event)
	{
		_events.Writer.TryWrite(@event);
	}
}

using System.Threading.Channels;

namespace Basil.Domain.Events;

public interface IEventSource<T> where T : Event
{
	ChannelReader<T> Events { get; }
}
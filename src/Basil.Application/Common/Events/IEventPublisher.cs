using System.Threading.Channels;

namespace Basil.Application.Common.Events;

/// <summary>
///     Exposes a channel of events produced by a runtime object.
/// </summary>
/// <typeparam name="T">The root event type the object emits.</typeparam>
public interface IEventPublisher<T> where T : Event
{
	ChannelReader<T> Events { get; }
}
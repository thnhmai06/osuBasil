using System.Threading.Channels;
using Basil.Application.Services.Contracts.Beatmaps;

namespace Basil.Application.Services.Implementations.Beatmaps;

/// <summary>The event stream shared by the beatmapset service and its objects.</summary>
internal sealed class BeatmapsetEventStream : IBeatmapsetEventSink
{
	private readonly Channel<BeatmapsetEvent> _events = Channel.CreateUnbounded<BeatmapsetEvent>();

	/// <summary>Gets the reader of the events emitted so far.</summary>
	internal ChannelReader<BeatmapsetEvent> Reader => _events.Reader;

	/// <summary>Emits an event to the stream.</summary>
	/// <param name="event">The event to emit.</param>
	internal void Emit(BeatmapsetEvent @event)
	{
		_events.Writer.TryWrite(@event);
	}

	void IBeatmapsetEventSink.Emit(BeatmapsetEvent @event) => Emit(@event);
}
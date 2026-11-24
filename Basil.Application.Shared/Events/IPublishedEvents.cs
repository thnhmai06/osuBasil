namespace Basil.Application.Shared.Events;

/// <summary>Lets a caller wait until the events published by its own request have been delivered.</summary>
public interface IPublishedEvents
{
	/// <summary>Starts tracking the events published from the current flow of execution, until the scope is disposed.</summary>
	IPublishedEventsScope Track();
}

/// <summary>The events published since <see cref="IPublishedEvents.Track"/>.</summary>
public interface IPublishedEventsScope : IDisposable
{
	/// <summary>Completes when those events have been delivered, or when <paramref name="limit"/> has passed, whichever comes first; events published by their handlers are not awaited.</summary>
	Task DeliveredAsync(TimeSpan limit, CancellationToken ct = default);
}

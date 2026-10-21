namespace Basil.Application.Runtime.Events;

/// <summary>
///     Marks a type as a runtime event: a record of something that has already happened to an
///     application object and may be observed by handlers.
/// </summary>
public abstract record Event
{
	/// <summary>Gets the moment the event was published.</summary>
	public DateTimeOffset Timestamp { get; init; }
}
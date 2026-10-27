using Mediator;

namespace Basil.Common.Application.Events;

/// <summary>
///     Marks a type as a runtime event: a record of something that has already happened to an
///     application object and may be observed by handlers.
/// </summary>
public abstract record Event : INotification
{
	/// <summary>Gets the moment the event was published.</summary>
	/// <remarks>Set once, by the publisher, before the event reaches any handler.</remarks>
	public DateTimeOffset Timestamp { get; internal set; }
}
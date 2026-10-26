namespace Basil.Application.Runtime.Events;

/// <summary>One published event on its way to the handlers that observe it.</summary>
internal interface IEventDelivery
{
	/// <summary>Hands the event to every handler that observes it, in turn.</summary>
	/// <param name="cancellationToken">A token that cancels the delivery when the server stops.</param>
	/// <remarks>A handler that fails is reported and does not stop the handlers after it.</remarks>
	ValueTask DeliverAsync(CancellationToken cancellationToken);
}
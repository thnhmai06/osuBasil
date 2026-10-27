using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Common.Application.Events;

/// <summary>Turns a published event into a delivery on the event queue instead of running its handlers at once.</summary>
/// <param name="queue">The queue that holds deliveries.</param>
/// <param name="logger">The logger that reports handler failures.</param>
public sealed class QueuedNotificationPublisher(EventQueue queue, ILogger<QueuedNotificationPublisher> logger)
	: INotificationPublisher
{
	/// <inheritdoc />
	public ValueTask Publish<TNotification>(NotificationHandlers<TNotification> handlers,
		TNotification notification, CancellationToken cancellationToken) where TNotification : INotification
	{
		queue.Enqueue(new Delivery<TNotification>(handlers, notification, logger));
		return ValueTask.CompletedTask;
	}

	private sealed class Delivery<TNotification>(
		NotificationHandlers<TNotification> handlers,
		TNotification notification,
		ILogger logger) : IEventDelivery where TNotification : INotification
	{
		public async ValueTask DeliverAsync(CancellationToken cancellationToken)
		{
			foreach (var handler in handlers)
				try
				{
					await handler.Handle(notification, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					return;
				}
				catch (Exception exception)
				{
					logger.LogError(exception, "A handler of {Event} failed", typeof(TNotification).Name);
				}
		}
	}
}
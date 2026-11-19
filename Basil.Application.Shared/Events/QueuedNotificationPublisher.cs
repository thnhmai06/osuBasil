using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Shared.Events;

public sealed class QueuedNotificationPublisher(EventQueue queue, ILogger<QueuedNotificationPublisher> logger)
	: INotificationPublisher
{
	public ValueTask Publish<TNotification>(NotificationHandlers<TNotification> handlers,
		TNotification notification, CancellationToken ct) where TNotification : INotification
	{
		queue.Enqueue(new Delivery<TNotification>(handlers, notification, logger, queue));
		return ValueTask.CompletedTask;
	}

	private sealed class Delivery<TNotification>(
		NotificationHandlers<TNotification> handlers,
		TNotification notification,
		ILogger logger,
		EventQueue queue) : IEventDelivery where TNotification : INotification
	{
		public async ValueTask DeliverAsync(CancellationToken ct)
		{
			foreach (var handler in handlers)
				try
				{
					await handler.Handle(notification, ct);
				}
				catch (OperationCanceledException) when (ct.IsCancellationRequested)
				{
					return;
				}
				catch (Exception exception)
				{
					queue.CountFailure();
					logger.LogError(exception, "A handler of {Event} failed", typeof(TNotification).Name);
				}
		}
	}
}
using System.Diagnostics;
using Basil.Infrastructure.Diagnostics.Metrics;
using Mediator;

namespace Basil.Infrastructure.Diagnostics.Pipeline;

internal sealed class MessageMeasurement<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
	where TMessage : notnull, IMessage
{
	public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next,
		CancellationToken ct)
	{
		var started = Stopwatch.GetTimestamp();
		try
		{
			return await next(message, ct);
		}
		finally
		{
			BasilMeter.MessageDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
				new KeyValuePair<string, object?>(BasilMeter.MessageTag, typeof(TMessage).Name));
		}
	}
}
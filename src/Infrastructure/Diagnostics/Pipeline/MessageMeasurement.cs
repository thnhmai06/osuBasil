using System.Diagnostics;
using Mediator;

namespace Basil.Infrastructure.Diagnostics.Pipeline;

/// <summary>Records on the diagnostics meter how long every request takes to be handled, tagged with its type.</summary>
/// <typeparam name="TMessage">The type of the message.</typeparam>
/// <typeparam name="TResponse">The type of the answer.</typeparam>
internal sealed class MessageMeasurement<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
	where TMessage : notnull, IMessage
{
	/// <inheritdoc />
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
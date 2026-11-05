using System.Diagnostics.Metrics;
using Basil.Infrastructure.Diagnostics.Pipeline;
using Mediator;

namespace Basil.Infrastructure.Diagnostics.Tests.Pipeline;

/// <summary>Every request is timed on the diagnostics meter, tagged with the name of its type and nothing else.</summary>
public sealed class MessageMeasurementTests
{
	[Fact]
	public async Task Handle_RecordsOneDurationTaggedWithTheMessageType_AndReturnsTheAnswer()
	{
		var seen = new List<(string Name, string? Message, int Tags)>();
		var duration = BasilMeter.MessageDuration; // created before the listener starts, or it is never published to it
		using var listener = new MeterListener();
		listener.InstrumentPublished = (instrument, meterListener) =>
		{
			if (ReferenceEquals(instrument, duration)) meterListener.EnableMeasurementEvents(instrument);
		};
		listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
		{
			string? message = null;
			foreach (var tag in tags)
				if (tag.Key == BasilMeter.MessageTag)
					message = tag.Value as string;
			seen.Add((instrument.Name, message, tags.Length));
		});
		listener.Start();

		var answer = await new MessageMeasurement<Ask, int>().Handle(new Ask(), (_, _) => ValueTask.FromResult(42),
			TestContext.Current.CancellationToken);

		Assert.Equal(42, answer);
		Assert.Equal([("basil.mediator.message.duration", nameof(Ask), 1)], seen);
	}

	private sealed record Ask : IRequest<int>;
}
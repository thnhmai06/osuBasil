using System.Net;
using Basil.Tests.Shared.Clients.Rest;
using Basil.Tests.Shared.Workload.Outcomes;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>Watches one live HTTP stream: the open and first event are one <c>sse</c> step, and a stream the server ends before the watcher stops is an error.</summary>
internal static class SseWatch
{
	/// <summary>Opens a stream and reads it until it ends or <paramref name="stop"/> is cancelled.</summary>
	/// <param name="workload">The shared collaborators.</param>
	/// <param name="rest">The client that opens the stream; its token is sent when <paramref name="authorize"/> is set.</param>
	/// <param name="path">The route of the stream.</param>
	/// <param name="authorize">Whether the stream needs the token.</param>
	/// <param name="context">Gives the scenario context to report to; read each time a step is recorded.</param>
	/// <param name="stop">Ends the watch; a stream that ends because of it is not an error.</param>
	/// <returns>Whether the stream opened and delivered; <see langword="false"/> when it was refused or empty.</returns>
	public static async Task<bool> WatchAsync(WorkloadContext workload, RestClient rest, string path, bool authorize,
		Func<NBomber.Contracts.IScenarioContext> context, CancellationToken stop)
	{
		SseStream? stream = null;
		IAsyncEnumerator<SseEvent>? events = null;
		Task<bool>? first = null;

		try
		{
			var opened = await StepExecution.RunAsync(Channels.Sse, context(), workload.Outcomes, async () =>
			{
				stream = await rest.OpenStreamAsync(path, authorize, stop);
				var open = OutcomeClassifier.FromHttpStatus(stream.Status,
					refusalExpected: stream.Status == HttpStatusCode.NotFound);
				if (open.Outcome != Outcome.Ok) return open;

				events = stream.ReadAsync(stop).GetAsyncEnumerator(stop);
				first = events.MoveNextAsync().AsTask();
				try
				{
					return await first.WaitAsync(workload.WaitTimeout, stop)
						? StepOutcome.Ok("sse")
						: StepOutcome.Error("sse_empty");
				}
				catch (TimeoutException)
				{
					// An open stream may stay quiet; it is only wrong when the server ends it.
					return StepOutcome.Ok("sse_open");
				}
			}, stop);
			if (opened.Outcome != Outcome.Ok || events is null) return false;

			var more = true;
			while (more)
			{
				more = await (first ?? events.MoveNextAsync().AsTask());
				first = null;
			}

			if (!stop.IsCancellationRequested)
				await StepExecution.RunAsync(Channels.Sse, context(), workload.Outcomes,
					() => Task.FromResult(StepOutcome.Error("sse_dropped")), stop);
			return true;
		}
		finally
		{
			try
			{
				if (first is not null) await first.WaitAsync(TimeSpan.FromSeconds(2));
				if (events is not null) await events.DisposeAsync();
			}
			catch (Exception)
			{
				// The stream is being abandoned; a read that cannot finish is dropped with it.
			}

			stream?.Dispose();
		}
	}
}

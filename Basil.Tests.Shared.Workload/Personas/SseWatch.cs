using System.Net;
using System.Text.Json;
using Basil.Tests.Shared.Clients.Rest;
using Basil.Tests.Shared.Workload.Outcomes;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>
///     Watches one live HTTP stream: the open and first event are one <c>sse</c> step, and a stream the server ends
///     before the watcher stops is an error.
/// </summary>
internal static class SseWatch
{
	// The server ends a room stream with a "closed" event when the room closes, and a match report stream once the
	// match has ended: the first event says so for a finished match, a second look says so for one that just ended.
	private static async Task<bool> EndedByDesignAsync(RestClient rest, string path, bool authorize, string? opening,
		string? closing, CancellationToken stop)
	{
		if (!path.StartsWith("/v1/matches/", StringComparison.Ordinal))
			return Find(closing, "type") is { ValueKind: JsonValueKind.String } type && type.GetString() == "closed";

		if (HasEnded(opening)) return true;

		try
		{
			using var again = await rest.OpenStreamAsync(path, authorize, stop);
			await foreach (var ev in again.ReadAsync(stop)) return HasEnded(ev.Data);
			return false;
		}
		catch (Exception exception) when (exception is HttpRequestException or IOException
			                                  or OperationCanceledException)
		{
			return false;
		}
	}

	private static bool HasEnded(string? report)
	{
		return Find(report, "match") is { ValueKind: JsonValueKind.Object } match &&
		       match.TryGetProperty("endedAt", out var endedAt) && endedAt.ValueKind == JsonValueKind.String;
	}

	// A property of an event's JSON, whether it sits at the root or inside the response envelope's data.
	private static JsonElement? Find(string? json, string property)
	{
		if (json is null) return null;

		using var document = JsonDocument.Parse(json);
		var root = document.RootElement;
		if (root.ValueKind != JsonValueKind.Object) return null;

		if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object) root = data;
		return root.TryGetProperty(property, out var value) ? value.Clone() : null;
	}

	/// <summary>Opens a stream and reads it until it ends or <paramref name="stop" /> is cancelled.</summary>
	/// <param name="workload">The shared collaborators.</param>
	/// <param name="rest">The client that opens the stream; its token is sent when <paramref name="authorize" /> is set.</param>
	/// <param name="path">The route of the stream.</param>
	/// <param name="authorize">Whether the stream needs the token.</param>
	/// <param name="context">Gives the scenario context to report to; read each time a step is recorded.</param>
	/// <param name="stop">Ends the watch; a stream that ends because of it is not an error.</param>
	/// <returns>Whether the stream opened and delivered; <see langword="false" /> when it was refused or empty.</returns>
	public static async Task<bool> WatchAsync(WorkloadContext workload, RestClient rest, string path, bool authorize,
		Func<IScenarioContext> context, CancellationToken stop)
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
					stream.Status == HttpStatusCode.NotFound);
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

			string? opening = null, closing = null;
			while (await (first ?? events.MoveNextAsync().AsTask()))
			{
				first = null;
				closing = events.Current.Data;
				opening ??= closing;
			}

			first = null;
			if (!stop.IsCancellationRequested &&
			    !await EndedByDesignAsync(rest, path, authorize, opening, closing, stop))
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
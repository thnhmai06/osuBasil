using Basil.Host.Rest.Http;

namespace Basil.Host.Rest.Sse;

/// <summary>Writes an SSE response: the snapshot first, then every chunk the hub gives the stream.</summary>
internal sealed class SseResult(SseHub hub, SseStream stream, byte[] snapshot, Action? onClosed = null) : IResult
{
	/// <inheritdoc />
	public async Task ExecuteAsync(HttpContext httpContext)
	{
		var response = httpContext.Response;
		response.ContentType = "text/event-stream";
		response.Headers.CacheControl = "no-cache";
		response.Headers["X-Accel-Buffering"] = "no";
		var cancellationToken = httpContext.RequestAborted;
		try
		{
			await response.Body.WriteAsync(snapshot, cancellationToken);
			await response.Body.FlushAsync(cancellationToken);
			hub.Touch(stream);
			await foreach (var chunk in stream.Reader.ReadAllAsync(cancellationToken))
			{
				await response.Body.WriteAsync(chunk, cancellationToken);
				await response.Body.FlushAsync(cancellationToken);
				hub.Touch(stream);
			}
		}
		catch (OperationCanceledException)
		{
			// The client went away; there is nothing left to tell it.
		}
		finally
		{
			hub.Release(stream);
			onClosed?.Invoke();
		}
	}
}

/// <summary>Opens SSE streams for the routes.</summary>
internal static class SseRoutes
{
	/// <summary>Opens a stream on a root and answers with it, or with the reason none could be opened.</summary>
	public static async Task<IResult> StreamAsync(this HttpContext context, string key,
		Func<CancellationToken, Task<(object Data, string? Id)>> snapshot, Action? onClosed = null,
		Func<CancellationToken, Task<SsePoll>>? poll = null)
	{
		var hub = context.RequestServices.GetRequiredService<SseHub>();
		var (stream, first, refusal) = await hub.OpenAsync(key, context.ConnectionOrNull(), context.Ip(), snapshot,
			context.RequestAborted, poll);
		if (refusal is not null)
		{
			if (hub.Stopping) context.Response.Headers.RetryAfter = RestPipeline.RetryAfterWhileStopping;
			return refusal;
		}

		return new SseResult(hub, stream!, first!, onClosed);
	}
}

/// <summary>Answers with a snapshot and ends, for a source that will never change again.</summary>
internal sealed class SnapshotOnlyResult(byte[] snapshot) : IResult
{
	/// <inheritdoc />
	public async Task ExecuteAsync(HttpContext httpContext)
	{
		var response = httpContext.Response;
		response.ContentType = "text/event-stream";
		response.Headers.CacheControl = "no-cache";
		await response.Body.WriteAsync(snapshot, httpContext.RequestAborted);
	}
}

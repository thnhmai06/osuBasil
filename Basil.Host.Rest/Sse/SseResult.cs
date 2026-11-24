using Basil.Host.Rest.Http;

namespace Basil.Host.Rest.Sse;

internal sealed class SseResult(SseHub hub, SseStream stream, byte[] snapshot, Action? onClosed = null) : IResult
{
	public async Task ExecuteAsync(HttpContext httpContext)
	{
		var response = httpContext.Response;
		response.ContentType = "text/event-stream";
		response.Headers.CacheControl = "no-cache";
		response.Headers["X-Accel-Buffering"] = "no";
		var ct = httpContext.RequestAborted;
		try
		{
			await response.Body.WriteAsync(snapshot, ct);
			await response.Body.FlushAsync(ct);
			hub.Touch(stream);
			await foreach (var chunk in stream.Reader.ReadAllAsync(ct))
			{
				await response.Body.WriteAsync(chunk, ct);
				await response.Body.FlushAsync(ct);
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

internal static class SseRoutes
{
	public static async Task<IResult> StreamAsync(this HttpContext context, string key,
		Func<CancellationToken, Task<(object Data, string? Id)>> snapshot, Action? onClosed = null,
		Func<CancellationToken, Task<SsePoll>>? poll = null)
	{
		var hub = context.RequestServices.GetRequiredService<SseHub>();
		var (stream, first, refusal) = await hub.OpenAsync(key, context.ConnectionOrNull(), snapshot,
			context.RequestAborted, poll);
		if (refusal is not null)
		{
			if (hub.Stopping) context.Response.Headers.RetryAfter = RestPipeline.RetryAfterWhileStopping;
			return refusal;
		}

		return new SseResult(hub, stream!, first!, onClosed);
	}
}

internal sealed class SnapshotOnlyResult(byte[] snapshot) : IResult
{
	public async Task ExecuteAsync(HttpContext httpContext)
	{
		var response = httpContext.Response;
		response.ContentType = "text/event-stream";
		response.Headers.CacheControl = "no-cache";
		await response.Body.WriteAsync(snapshot, httpContext.RequestAborted);
	}
}
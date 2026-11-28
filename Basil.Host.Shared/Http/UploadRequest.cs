using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Basil.Host.Shared.Http;

/// <summary>Bounds an upload request by its kind's limit and a minimum data rate, and cuts the connection of one that must be refused.</summary>
public static class UploadRequest
{
	/// <summary>The room the form's own fields take beside the file.</summary>
	private const long FormSlack = 64 * 1024;

	private static readonly MinDataRate MinimumRate = new(1024, TimeSpan.FromSeconds(5));

	/// <summary>Sets the most bytes the request may carry and the slowest rate it may arrive at.</summary>
	/// <param name="context">The upload request, before its body is read.</param>
	/// <param name="fileLimit">The most bytes the file of this kind of upload may have.</param>
	/// <returns>False when the request declares no length or more than that, so its body must not be read.</returns>
	public static bool Accept(HttpContext context, long fileLimit)
	{
		var limit = fileLimit + FormSlack;
		if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
			size.MaxRequestBodySize = limit;
		if (context.Features.Get<IHttpMinRequestBodyDataRateFeature>() is { } rate)
			rate.MinDataRate = MinimumRate;
		// A body without a declared length could not be charged to the hourly upload allowance, so it is refused too.
		return context.Request.ContentLength is { } declared && declared <= limit;
	}

	/// <summary>Runs the reading of an upload; when it is refused, logs why and cuts the connection.</summary>
	/// <param name="context">The upload request.</param>
	/// <param name="route">The route, for the log.</param>
	/// <param name="caller">The session or account the upload comes from, for the log.</param>
	/// <param name="run">Reads the upload and answers it.</param>
	public static async Task<IResult> GuardAsync(HttpContext context, string route, string caller,
		Func<Task<IResult>> run)
	{
		UploadRefusal reason;
		try
		{
			return await run();
		}
		catch (UploadRefusedException refused)
		{
			reason = refused.Reason;
		}
		catch (Microsoft.AspNetCore.Http.BadHttpRequestException bad)
		{
			reason = bad.StatusCode switch
			{
				StatusCodes.Status413PayloadTooLarge => UploadRefusal.TooLarge,
				StatusCodes.Status408RequestTimeout => UploadRefusal.TooSlow,
				_ => UploadRefusal.Malformed
			};
		}

		Refuse(context, route, caller, reason);
		return Results.Empty;
	}

	/// <summary>Logs a refused upload and cuts its connection.</summary>
	public static void Refuse(HttpContext context, string route, string caller, UploadRefusal reason)
	{
		context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(UploadRequest).FullName!)
			.LogWarning("Upload refused: {Reason} on {Route} from {Caller} at {Address}, declared {Declared} bytes.",
				reason, route, caller, ClientAddress.Resolve(context), context.Request.ContentLength);
		context.Abort();
	}
}

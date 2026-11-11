using Basil.Application.Shared.Settings;
using Basil.Gateway.RateLimiting;
using Mediator;
using System.Globalization;
using System.Text.Json;
using Basil.Application.Users.Registry;
using Basil.Gateway;
using Basil.Host.Rest.Sse;
using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Basil.Host.Rest.Http;

/// <summary>
///     The first steps of the API pipeline: turns an unhandled exception into a 500 answer, authenticates the
///     caller and applies the rate limits that are read from the server settings.
/// </summary>
internal sealed class RestPipeline(RequestDelegate next)
{
	/// <summary>The seconds a client waits before it tries again while the server is stopping.</summary>
	internal const string RetryAfterWhileStopping = "5";

	/// <summary>Runs the steps for one request.</summary>
	public async Task InvokeAsync(HttpContext context, UserRegistry registry, ISender sender,
		RateLimiter limiter, SseHub hub, ILogger<RestPipeline> logger)
	{
		if (hub.Stopping)
		{
			context.Response.Headers.RetryAfter = RetryAfterWhileStopping;
			await Api.Error(StatusCodes.Status503ServiceUnavailable, "shuttingDown", "The server is shutting down.")
				.ExecuteAsync(context);
			return;
		}

		try
		{
			var authenticated = await context.AuthenticateAsync(registry);
			if (await RateLimitedAsync(context, authenticated, sender, limiter)) return;

			await next(context);
		}
		catch (Exception ex) when (!context.Response.HasStarted && ex is BadHttpRequestException or JsonException)
		{
			await RefuseAsync(context, ex);
		}
		catch (Exception ex) when (!context.Response.HasStarted && ex is not OperationCanceledException)
		{
			await FailAsync(context, logger, ex);
		}
	}

	private static async Task<bool> RateLimitedAsync(HttpContext context, bool authenticated,
		ISender sender,
		RateLimiter limiter)
	{
		var limits = (await sender.Send(new ReadSettings(), context.RequestAborted)).RateLimits.Rest;
		var mutation = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) &&
		               !HttpMethods.IsOptions(context.Request.Method);
		if (authenticated && mutation)
		{
			var user = context.Connection().Record.Id;
			if (limiter.TryAcquire($"rest-mutation:{user}", limits.Mutations.Count, limits.Mutations.Window))
				return false;

			await TooManyAsync(context, limits.Mutations.Window);
			return true;
		}

		if (authenticated ||
		    limiter.TryAcquire($"rest-anonymous:{context.Ip()}", limits.Anonymous.PerIp.Count,
			    limits.Anonymous.PerIp.Window))
			return false;

		await TooManyAsync(context, limits.Anonymous.PerIp.Window);
		return true;
	}

	private static Task RefuseAsync(HttpContext context, Exception ex)
	{
		context.Response.Clear();
		var status = ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status400BadRequest;
		return Api.Error(status, status == StatusCodes.Status413PayloadTooLarge ? "tooLarge" : "invalid",
				"The request could not be read: a parameter or the body is malformed or out of range.")
			.ExecuteAsync(context);
	}

	private static async Task FailAsync(HttpContext context, ILogger<RestPipeline> logger, Exception ex)
	{
		logger.LogError(ex, "Unhandled exception in {Route}", context.GetEndpoint()?.DisplayName ?? "an unknown route");
		context.Response.Clear();
		await Api.Error(StatusCodes.Status500InternalServerError, "internalError", "Internal server error.")
			.ExecuteAsync(context);
	}

	private static Task TooManyAsync(HttpContext context, TimeSpan retryAfter)
	{
		context.Response.Headers.RetryAfter =
			((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
		return Api.Error(StatusCodes.Status429TooManyRequests, "rateLimited", "Too many requests.")
			.ExecuteAsync(context);
	}
}

/// <summary>Reads the allowed origins from the server settings on each request, so a change applies at once.</summary>
internal sealed class SettingsCorsPolicyProvider(ISender sender) : ICorsPolicyProvider
{
	/// <inheritdoc />
	public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
	{
		var origins = (await sender.Send(new ReadSettings(), context.RequestAborted)).Cors.AllowedOrigins;
		return new CorsPolicyBuilder().WithOrigins(origins).AllowAnyMethod()
			.WithHeaders("Authorization", "Content-Type", "Last-Event-ID").Build();
	}
}
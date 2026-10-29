using System.Globalization;
using Basil.Application.Content.Persistence;
using Basil.Application.Users.Users;
using Basil.Host.Rest.Sse;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Logging;

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
	public async Task InvokeAsync(HttpContext context, UserRegistry registry, ISettingsRepository settings,
		RateLimiter limiter, SseHub hub, ILogger<RestPipeline> logger)
	{
		if (hub.Stopping)
		{
			context.Response.Headers.RetryAfter = RetryAfterWhileStopping;
			await Api.Error(StatusCodes.Status503ServiceUnavailable, "shuttingDown", "The server is shutting down.").ExecuteAsync(context);
			return;
		}

		try
		{
			var authenticated = await context.AuthenticateAsync(registry);
			var limits = (await settings.GetAsync(context.RequestAborted)).RateLimits.Rest;
			var mutation = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) &&
			               !HttpMethods.IsOptions(context.Request.Method);
			if (authenticated && mutation)
			{
				var user = context.Connection().Record.Id;
				if (!limiter.TryAcquire($"rest-mutation:{user}", limits.Mutations.Count, limits.Mutations.Window))
				{
					await TooManyAsync(context, limits.Mutations.Window);
					return;
				}
			}
			else if (!authenticated &&
			         !limiter.TryAcquire($"rest-anonymous:{context.Ip()}", limits.Anonymous.PerIp.Count,
				         limits.Anonymous.PerIp.Window))
			{
				await TooManyAsync(context, limits.Anonymous.PerIp.Window);
				return;
			}

			await next(context);
		}
		catch (Exception ex) when (!context.Response.HasStarted && ex is not OperationCanceledException)
		{
			logger.LogError(ex, "Unhandled exception in {Path}", context.Request.Path);
			context.Response.Clear();
			await Api.Error(StatusCodes.Status500InternalServerError, "internalError", "Internal server error.")
				.ExecuteAsync(context);
		}
	}

	private static Task TooManyAsync(HttpContext context, TimeSpan retryAfter)
	{
		context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
		return Api.Error(StatusCodes.Status429TooManyRequests, "rateLimited", "Too many requests.").ExecuteAsync(context);
	}
}

/// <summary>Reads the allowed origins from the server settings on each request, so a change applies at once.</summary>
internal sealed class SettingsCorsPolicyProvider(ISettingsRepository settings) : ICorsPolicyProvider
{
	/// <inheritdoc />
	public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
	{
		var origins = (await settings.GetAsync(context.RequestAborted)).Cors.AllowedOrigins;
		return new CorsPolicyBuilder().WithOrigins(origins).AllowAnyMethod()
			.WithHeaders("Authorization", "Content-Type", "Last-Event-ID").Build();
	}
}

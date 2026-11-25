using Basil.Application.Sessions.Registry;
using Basil.Domain;
using System.Text.Json;
using Basil.Application.Shared.Settings;
using Basil.Host.Rest.Sse;
using Mediator;
using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Basil.Host.Rest.Http;

internal sealed class RestPipeline(RequestDelegate next)
{
	internal const string RetryAfterWhileStopping = "5";

	public async Task InvokeAsync(HttpContext context, SessionRegistry registry, ISender sender,
		SseHub hub, ILogger<RestPipeline> logger)
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
			await context.AuthenticateAsync(sender);
			await next(context);
		}
		catch (Exception ex) when (!context.Response.HasStarted && ex is BadHttpRequestException or JsonException)
		{
			await RefuseAsync(context, ex);
		}
		catch (BasilException error) when (!context.Response.HasStarted)
		{
			context.Response.Clear();
			var info = BusinessErrors.Describe(error);
			await Api.Error(OutcomeResults.StatusOf(info.Kind), info.Code, OutcomeTexts.Of(info.Code)).ExecuteAsync(context);
		}
		catch (Exception ex) when (!context.Response.HasStarted && ex is not OperationCanceledException)
		{
			await FailAsync(context, logger, ex);
		}
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
}

internal sealed class SettingsCorsPolicyProvider(ISender sender) : ICorsPolicyProvider
{
	public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
	{
		var origins = (await sender.Send(new ReadSettings(), context.RequestAborted)).Cors.AllowedOrigins;
		return new CorsPolicyBuilder().WithOrigins(origins).AllowAnyMethod()
			.WithHeaders("Authorization", "Content-Type", "Last-Event-ID").Build();
	}
}
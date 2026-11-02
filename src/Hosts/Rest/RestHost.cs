using Basil.Host.Rest.Http;
using Basil.Host.Rest.Routes;
using Basil.Host.Rest.Sse;
using Basil.Host.Shared.Diagnostics;
using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Basil.Host.Rest;

/// <summary>Maps the HTTP API of the <c>api.</c> host.</summary>
public static class RestHost
{
	/// <summary>Adds the services of the live streams and the diagnostics, which need nothing of the web server.</summary>
	/// <param name="services">The service collection.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddRestHost(this IServiceCollection services)
	{
		services.AddSingleton<RateLimiter>();
		services.AddSingleton<LoginAttemptGuard>();
		services.AddSingleton<RuntimeSampler>(sp => new RuntimeSampler(sp.GetRequiredService<TimeProvider>()));
		services.AddHostedService<DiagnosticsStarter>();
		services.AddSingleton<SseHub>();
		services.AddHostedService(sp => sp.GetRequiredService<SseHub>());
		return services;
	}

	/// <summary>Adds the web services of the HTTP API: documentation, CORS and the JSON options.</summary>
	/// <param name="services">The service collection.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddRestApi(this IServiceCollection services)
	{
		services.AddBasilOpenApi();
		// Bad parameters throw instead of answering an empty 400, so that the pipeline answers with the envelope.
		services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
		services.AddCors();
		services.AddSingleton<ICorsPolicyProvider, SettingsCorsPolicyProvider>();
		services.ConfigureHttpJsonOptions(options =>
		{
			options.SerializerOptions.PropertyNamingPolicy = Api.Json.PropertyNamingPolicy;
			foreach (var converter in Api.Json.Converters) options.SerializerOptions.Converters.Add(converter);
		});
		return services;
	}

	/// <summary>Serves the HTTP API for the requests addressed to the <c>api.</c> sub-domain.</summary>
	/// <param name="app">The application pipeline.</param>
	/// <returns><paramref name="app" />, for chaining.</returns>
	public static IApplicationBuilder UseRest(this IApplicationBuilder app)
	{
		app.MapWhen(context => context.Request.Host.Host.StartsWith("api.", StringComparison.OrdinalIgnoreCase),
			branch =>
			{
				branch.UseMiddleware<RestPipeline>();
				branch.UseCors();
				branch.UseRouting();
				branch.UseEndpoints(endpoints =>
				{
					endpoints.MapRest();
					endpoints.MapFallback(NoSuchRoute);
				});
			});
		return app;
	}

	// A number in the path that does not fit an int makes `{id:int}` fail to match: say so instead of "no such route".
	private static IResult NoSuchRoute(HttpContext context)
	{
		var overflow = context.Request.Path.Value!.Split('/')
			.Any(segment => segment.TrimStart('-') is { Length: > 0 } digits && digits.All(char.IsAsciiDigit) &&
			                !int.TryParse(segment, out _));
		return overflow
			? Api.Invalid("A number in the path is out of range.")
			: Api.NotFound("No such route.");
	}

	private static void MapRest(this IEndpointRouteBuilder app)
	{
		app.MapGet("/health", () => Api.Ok("healthy")).WithTags("Operations")
			.WithSummary("Check that the server answers.");
		app.MapDocs();
		var v1 = app.MapGroup("/v1");
		v1.MapAuth();
		v1.MapUsers();
		v1.MapRooms();
		v1.MapMatches();
		v1.MapDiagnostics();
		v1.MapChannels();
		v1.MapBeatmaps();
		v1.MapScores();
		v1.MapContent();
		v1.MapMenu();
		v1.MapAliases();
	}
}
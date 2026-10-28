using Basil.Host.Rest.Http;
using Basil.Host.Rest.Routes;
using Basil.Host.Rest.Sse;
using Basil.Host.Shared.Diagnostics;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http.Json;

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
		app.MapWhen(context => context.Request.Host.Host.StartsWith("api.", StringComparison.OrdinalIgnoreCase), branch =>
		{
			branch.UseMiddleware<RestPipeline>();
			branch.UseCors();
			branch.UseRouting();
			branch.UseEndpoints(endpoints =>
			{
				endpoints.MapRest();
				endpoints.MapFallback(() => Api.NotFound("No such route."));
			});
		});
		return app;
	}

	private static void MapRest(this IEndpointRouteBuilder app)
	{
		app.MapGet("/health", () => Api.Ok("healthy")).WithTags("Operations").WithSummary("Check that the server answers.");
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

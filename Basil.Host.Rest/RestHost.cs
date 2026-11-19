using Basil.Gateway.Admission;
using Basil.Gateway.Limits;
using Basil.Gateway.RateLimiting;
using Basil.Host.Rest.Http;
using Basil.Host.Rest.Routes;
using Basil.Host.Rest.Sse;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Basil.Host.Rest;

public static class RestHost
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddRestHost()
		{
			services.AddSingleton<RateLimiter>();
			services.AddSingleton<LoginAttemptGuard>();
			services.AddSingleton<LoadSignal>();
			services.AddSingleton<SseHub>();
			services.AddHostedService(sp => sp.GetRequiredService<SseHub>());
			return services;
		}

		public IServiceCollection AddRestApi(IConfiguration configuration)
		{
			services.AddOptions<RestOptions>().Configure(rest =>
				rest.Domain = configuration.GetValue("Basil:Server:Domain", rest.Domain)!);
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
	}

	public static IApplicationBuilder UseRest(this IApplicationBuilder app)
	{
		app.MapWhen(context => context.Request.Host.Host.StartsWith("api.", StringComparison.OrdinalIgnoreCase),
			branch =>
			{
				branch.UseMiddleware<RestPipeline>();
				branch.UseCors();
				branch.UseRouting();
				branch.UseMiddleware<RestAdmission>();
				branch.UseEndpoints(endpoints => endpoints.MapRest());
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

	internal static void MapRest(this IEndpointRouteBuilder endpoints)
	{
		var app = endpoints.MapGroup("").WithMetadata(new AdmissionPolicyAttribute());
		app.MapGet("/health", (Admission admission) => Api.Ok(admission.Health.ToString().ToLowerInvariant()))
			.WithTags("Operations")
			.WithSummary("Get the server health.")
			.WithDescription(
				"The state is `healthy`, `degraded` when the server is busier than usual, or `shedding` when it is overloaded and refuses the least valuable work.");
		app.MapDocs();
		app.MapGuides();
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
		v1.MapRulesets();
		v1.MapMenu();
		v1.MapAliases();
		app.MapFallback(NoSuchRoute);
	}
}
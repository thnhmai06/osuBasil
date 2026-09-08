using Basil.Server.Shared.Configuration;
using Basil.Server.Shared.Http;
using Basil.Server.Shared.Http.Middleware;
using SixLabors.ImageSharp.Web.DependencyInjection;

namespace Basil.Server.Host;

/// <summary>The host entry point.</summary>
public sealed class Bootstrap
{
	/// <summary>
	///     The host entry point: builds the web application, wires the middleware pipeline and host
	///     groups, initializes startup data, and runs the application.
	/// </summary>
	/// <param name="args">The command-line arguments passed to the host.</param>
	public static async Task Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);
		SerilogSetup.Configure(builder);

		ConfigurationSetup.Configure(builder, args);
		KestrelSetup.Configure(builder);
		builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection(ServerOptions.SectionName));
		ConfigureRouting(builder);

		SliceRegistration.AddAll(builder);
		JsonSetup.Configure(builder);
		OpenApiSetup.Configure(builder);
		AuthSetup.Configure(builder);
		CorsSetup.Configure(builder);
		ImageSharpSetup.Configure(builder);

		var app = builder.Build();
		StartupBanner.Log(app);

		// Order matters: authentication/authorization must run before EnvelopeMiddleware (which
		// needs the resolved role to decide what a response reveals), which must run before
		// ApiRequestLoggingMiddleware (which logs the final status code). RequestMetricsMiddleware
		// runs first so its measured duration includes every other middleware's cost.
		app.UseMiddleware<RequestMetricsMiddleware>();
		app.UseMiddleware<RequestIdLoggingMiddleware>();
		app.UseMiddleware<ExceptionLoggingMiddleware>();
		app.UseWebSockets();
		app.UseCors(CorsSetup.PolicyName);
		app.UseAuthentication();
		app.UseAuthorization();
		app.UseMiddleware<EnvelopeMiddleware>();
		app.UseMiddleware<ApiRequestLoggingMiddleware>();
		app.UseImageSharp();

		SliceRegistration.MapAll(app);

		var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
		var hostLogger = app.Services.GetRequiredService<ILogger<Bootstrap>>();
		lifetime.ApplicationStopping.Register(() => hostLogger.LogInformation("Server shutting down"));

		await StartupData.InitializeAsync(app);
		await app.RunAsync();
	}

	/// <summary>Registers the <c>:numericid</c> route constraint used by the `api.` host's id route parameters.</summary>
	/// <param name="builder">The web application builder whose routing options are configured.</param>
	private static void ConfigureRouting(WebApplicationBuilder builder)
	{
		builder.Services.Configure<RouteOptions>(options =>
			options.ConstraintMap[NumericIdRouteConstraint.Token] = typeof(NumericIdRouteConstraint));
	}
}
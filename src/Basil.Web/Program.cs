using System.Text.Json.Nodes;
using Basil.Application;
using Basil.Application.Abstractions.Channels;
using Basil.Application.Configuration;
using Basil.Application.Json;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions.Channels;
using Basil.Infrastructure.Beatmaps;
using Basil.Infrastructure.DependencyInjection;
using Basil.Infrastructure.Persistence;
using Basil.Web.Auth;
using Basil.Web.Logging;
using Basil.Web.Middleware;
using Basil.Web.OpenApi;
using Basil.Web.Routing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Events;

namespace Basil.Web;

public sealed class Program
{
	private const string CorsPolicyName = "ApiCors";

	/// <summary>
	///     One `.WithTags(...)` string per row, one line of description each, grouped into the Scalar
	///     sidebar's collapsible sections in this exact order: every route under one resource (e.g.
	///     every `/matches/...` tag) stays adjacent regardless of whether it happens to also support
	///     SSE, since SSE-vs-plain-JSON is no longer a tag of its own (content negotiation is called out
	///     in each route's own `.WithDescription` instead). Wired into the `basilapi` document as both
	///     `document.Tags` (descriptions) and the `x-tagGroups` extension Scalar reads for the sidebar's
	///     group order (see <see cref="AddOpenApiDocument" />).
	/// </summary>
	private static readonly (string Group, (string Tag, string Description)[] Tags)[] BasilApiTagGroups =
	[
		("Matches",
		[
			("Matches", "List and create matches."),
			("Match Report", "The tournament match report (TRT): a one-shot JSON snapshot (the live SSE " +
			                 "equivalent is under Match Live)."),
			("Match Settings", "Read/update a match's room configuration (name, password, map, mods, ...)."),
			("Match Live", "Room-wide \"currently playing\" status and the merged per-slot live stream."),
			("Match Hosts", "Get/set/clear the match host."),
			("Match Referees", "List/replace/add/remove the match's referees."),
			("Match Bans", "List/replace/add players banned from the match, and unban."),
			("Match Slots", "Read or reassign/re-team/lock the match's 16 slots (index-addressed list), " +
			                "invite players onto them, and kick a seated player."),
			("Match Timer", "Read, start, or abort the match's countdown timer."),
			("Match Abort", "Abort the match currently in progress."),
			("Match Close", "Close the match immediately.")
		]),
		("Users",
		[
			("Users", "User CRUD, avatar management, and live spectator-input streams.")
		]),
		("Beatmapsets",
		[
			("Beatmapsets", "Beatmapset CRUD, archive/storyboard downloads, and freeze/private management."),
			("Beatmaps", "Individual beatmap lookups and file/background downloads within a beatmapset.")
		]),
		("Scores",
		[
			("Scores", "Individual score lookups, replay downloads, and the paginated score list.")
		]),
		("FAQ",
		[
			("FAQ", "Public FAQ entry storage.")
		]),
		("Seasonal Backgrounds",
		[
			("Seasonal Backgrounds", "Public seasonal background image storage.")
		]),
		("Menu Icon",
		[
			("Menu Icon Image", "The in-game main menu icon image file."),
			("Menu Icon URL", "The click-through URL opened when a player clicks the main menu icon.")
		]),
		("Abbreviation Redirects",
		[
			("Abbreviation Redirects", "Short-prefix 302 redirects to the canonical plural resource paths.")
		]),
		("Health",
		[
			("Health", "Liveness check.")
		])
	];

	/// <summary>
	///     The host entry point: builds the web application, wires the middleware pipeline and host
	///     groups, initializes startup data, and runs the application.
	/// </summary>
	/// <remarks>
	///     The pipeline registers RequestIdLoggingMiddleware, Serilog request logging,
	///     ExceptionLoggingMiddleware, WebSockets, CORS, authentication/authorization, and finally
	///     EnvelopeMiddleware, in that order. Host groups are mapped from the configured domain
	///     (defaulting to localhost), a shutdown log line is registered against the host lifetime, and
	///     <see cref="InitializeDataAsync" /> runs before the application starts serving.
	/// </remarks>
	/// <param name="args">The command-line arguments passed to the host.</param>
	public static async Task Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);
		ConfigureSerilog(builder);

		ConfigureConfiguration(builder, args);
		ConfigureKestrel(builder);
		builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection(ServerOptions.SectionName));

		builder.Services.AddInfrastructure(builder.Configuration);
		builder.Services.AddApplication();
		ConfigureJson(builder);
		ConfigureOpenApi(builder);
		ConfigureAuth(builder);
		ConfigureCors(builder);

		var app = builder.Build();
		app.UseMiddleware<RequestIdLoggingMiddleware>();
		app.UseSerilogRequestLogging();
		app.UseMiddleware<ExceptionLoggingMiddleware>();
		app.UseWebSockets();
		app.UseCors(CorsPolicyName);
		app.UseAuthentication();
		app.UseAuthorization();
		app.UseMiddleware<EnvelopeMiddleware>();

		var domain = builder.Configuration.GetSection(ServerOptions.SectionName)["Domain"] ?? "localhost";
		BanchoHostGroups.MapAll(app, domain);

		var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
		var hostLogger = app.Services.GetRequiredService<ILogger<Program>>();
		lifetime.ApplicationStopping.Register(() => hostLogger.LogInformation("Server shutting down"));

		await InitializeDataAsync(app);
		await app.RunAsync();
	}

	/// <summary>
	///     Configures Serilog for the host, writing to the console and two daily-rolling file sinks.
	/// </summary>
	/// <remarks>
	///     Files are fixed at <c>Logs/</c> next to the executable (not under <c>Data/</c>: logs are
	///     operational output, not application data), and are not bound from appsettings or
	///     configurable. <c>Serilog.Sinks.File</c> creates <c>Logs/full</c> and <c>Logs/errors</c>
	///     (and therefore their parent <c>Logs/</c>, which the hardlink pointer files also live
	///     directly under) itself on first write, so no explicit <c>Directory.CreateDirectory</c>
	///     call is needed here. The minimum level (stdout plus the full file; the errors file always
	///     stays Error-only regardless) reads from <c>Basil:Logging:MinimumLevel</c>, defaulting to
	///     Information. <c>WebApplication.CreateBuilder</c> already loads appsettings.json before this
	///     runs, so the value is available here even though <see cref="ConfigureConfiguration" />
	///     (which re-layers it plus command-line args) has not run yet.
	/// </remarks>
	/// <param name="builder">The web application builder whose logging configuration is set up.</param>
	private static void ConfigureSerilog(WebApplicationBuilder builder)
	{
		var logsPath = Path.Combine(AppContext.BaseDirectory, "Logs");
		const string template =
			"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{Category}] {RequestId} {SourceContext}: {Message:lj} {Properties}{NewLine}{Exception}";

		var minimumLevel = Enum.TryParse<LogEventLevel>(
			builder.Configuration["Basil:Logging:MinimumLevel"], true, out var configuredLevel)
			? configuredLevel
			: LogEventLevel.Information;

		builder.Services.AddSerilog(lc => lc
			.Enrich.FromLogContext()
			.Enrich.With<CategoryEnricher>()
			.MinimumLevel.Is(minimumLevel)
			.MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
			// Anything the CategoryEnricher couldn't place in a curated scope (Mapsets/Matches/Scores/
			// Online/IRC/Host/Database/Cache) is generic framework/library chatter, not a domain event
			// worth Information-level noise — only Warning+ from it shows by default. Curated
			// categories, and anything at Warning+ regardless of category, are never touched here.
			.Filter.ByExcluding(e =>
				e.Level < LogEventLevel.Warning &&
				e.Properties.TryGetValue("Category", out var category) &&
				category is ScalarValue { Value: string categoryName } &&
				categoryName == CategoryEnricher.FallbackCategory)
			.WriteTo.Console(outputTemplate: template)
			.WriteTo.File(
				Path.Combine(logsPath, "full", "basil-.log"),
				rollingInterval: RollingInterval.Day,
				retainedFileCountLimit: 30,
				outputTemplate: template,
				hooks: new HardLinkFileLifecycleHooks(Path.Combine(logsPath, "latest.log")))
			.WriteTo.File(
				Path.Combine(logsPath, "errors", "basil-.log"),
				LogEventLevel.Error,
				rollingInterval: RollingInterval.Day,
				retainedFileCountLimit: 30,
				outputTemplate: template,
				hooks: new HardLinkFileLifecycleHooks(Path.Combine(logsPath, "errors_latest.log"))));
	}

	/// <summary>
	///     Re-layers configuration, adding environment-specific and command-line sources on top of appsettings.json.
	/// </summary>
	/// <remarks>
	///     <c>appsettings.json</c> carries all Basil settings under a <c>Basil</c> section alongside
	///     standard ASP.NET Core config (<c>Logging</c>, <c>AllowedHosts</c>).
	///     <c>appsettings.{EnvironmentName}.json</c> and command-line args are layered on top for
	///     environment-specific overrides.
	/// </remarks>
	/// <param name="builder">The web application builder whose configuration is extended.</param>
	/// <param name="args">The command-line arguments layered last, at the highest priority.</param>
	private static void ConfigureConfiguration(WebApplicationBuilder builder, string[] args)
	{
		builder.Configuration
			.AddJsonFile("appsettings.json", false, true)
			.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true, true)
			.AddCommandLine(args);
	}

	/// <summary>
	///     Configures the Kestrel endpoint and HTTPS certificate from the <c>Basil:Server</c> section.
	/// </summary>
	/// <remarks>
	///     The section supplies <c>Port</c> (default 443), <c>CertPath</c>, and <c>CertPassword</c>.
	///     Auto port selection is disabled, so the server binds exclusively on the configured port.
	///     Leaving <c>CertPath</c>/<c>CertPassword</c> unset uses the dev cert or OS-level TLS.
	///     <c>Http1AndHttp2</c> lets a browser multiplex several SSE connections over one connection
	///     instead of hitting HTTP/1.1's ~6-per-origin ceiling; it only takes effect over TLS, which
	///     every listener here already uses. A bad cert path or password is logged at Critical (path
	///     only, never the password) before the process exits with code 1.
	/// </remarks>
	/// <param name="builder">The web application builder whose Kestrel options are configured.</param>
	private static void ConfigureKestrel(WebApplicationBuilder builder)
	{
		builder.WebHost.ConfigureKestrel((context, options) =>
		{
			var serverSection = context.Configuration.GetSection(ServerOptions.SectionName);
			var port = serverSection.GetValue<int?>("Port") ?? 443;
			var certPath = serverSection["CertPath"];
			var certPassword = serverSection["CertPassword"];

			options.ConfigureEndpointDefaults(listenOptions =>
				listenOptions.Protocols = HttpProtocols.Http1AndHttp2);

			try
			{
				options.ListenAnyIP(port, listenOptions =>
				{
					if (!string.IsNullOrEmpty(certPath))
						listenOptions.UseHttps(certPath, certPassword);
					else
						listenOptions.UseHttps();
				});
			}
			catch (Exception ex)
			{
				// UseHttps(path, password) loads the certificate synchronously — a bad path/password
				// throws right here, previously with no clear log line before the generic host's own
				// crash handling took over. Fatal + explicit exit instead of letting that propagate
				// unclearly (never logs the password).
				options.ApplicationServices.GetService<ILoggerFactory>()
					?.CreateLogger("Basil.Web.Program")
					.LogCritical(ex, "Failed to load TLS certificate from {CertPath} — server cannot start.", certPath);
				Environment.Exit(1);
			}
		});
	}

	/// <summary>
	///     Registers the admin-key authentication scheme and the admin-role authorization policy.
	/// </summary>
	/// <remarks>
	///     The scheme reads the <c>X-Admin-Key</c> header (see
	///     <see cref="AdminKeyAuthenticationHandler" />) instead of the old AdminKeyFilter endpoint
	///     filter. One mechanism serves both the hard admin-only gate
	///     (<c>RequireAuthorization</c>) and the soft private/frozen-visibility elevation
	///     (<c>User.IsInRole</c>).
	/// </remarks>
	/// <param name="builder">The web application builder whose authentication services are registered.</param>
	private static void ConfigureAuth(WebApplicationBuilder builder)
	{
		builder.Services
			.AddAuthentication(AdminKeyDefaults.Scheme)
			.AddScheme<AuthenticationSchemeOptions, AdminKeyAuthenticationHandler>(AdminKeyDefaults.Scheme, null);

		builder.Services.AddAuthorization(options =>
			options.AddPolicy(AdminKeyDefaults.Policy, policy => policy.RequireRole(AdminKeyDefaults.Role)));
	}

	/// <summary>
	///     Registers <see cref="CountryJsonConverter" /> globally for every JSON response using the host defaults.
	/// </summary>
	/// <remarks>
	///     <see cref="CountryJsonConverter" /> is the only enum with a non-default wire form (see its
	///     own doc comment), and it applies to every <c>Results.Json(...)</c> call that does not pass
	///     its own <c>JsonSerializerOptions</c>. Every other enum keeps System.Text.Json's default
	///     numeric serialization, so there is no <c>JsonStringEnumConverter</c> anywhere. The
	///     converters are copied from <see cref="BasilJsonOptions.Instance" /> (the shared options
	///     every live SSE payload serializes with, see <c>SnapshotChannel</c>/<c>JsonMergePatch</c>)
	///     rather than constructing a fresh <see cref="CountryJsonConverter" />, so regular JSON
	///     responses and the live channel payloads never drift apart.
	///     <c>Microsoft.AspNetCore.Http.Json.JsonOptions.SerializerOptions</c> has no public setter,
	///     so the instance itself cannot be swapped for <see cref="BasilJsonOptions.Instance" />;
	///     copying its converters is the closest equivalent.
	/// </remarks>
	/// <param name="builder">The web application builder whose HTTP JSON options are configured.</param>
	private static void ConfigureJson(WebApplicationBuilder builder)
	{
		builder.Services.ConfigureHttpJsonOptions(options =>
		{
			foreach (var converter in BasilJsonOptions.Instance.Converters)
				options.SerializerOptions.Converters.Add(converter);
		});
	}

	/// <summary>
	///     Registers a permissive CORS policy allowing any origin, method, and header.
	/// </summary>
	/// <remarks>
	///     Permissive by design: the api. host is meant to be called directly from arbitrary
	///     browser-based tooling (tournament overlays, dashboards, OBS browser sources). No
	///     credentials are ever sent (<c>X-Admin-Key</c> is a plain header, not a cookie), so
	///     <c>AllowAnyOrigin</c> is safe here.
	/// </remarks>
	/// <param name="builder">The web application builder whose CORS policy is registered.</param>
	private static void ConfigureCors(WebApplicationBuilder builder)
	{
		builder.Services.AddCors(options =>
			options.AddPolicy(CorsPolicyName, policy => policy
				.AllowAnyOrigin()
				.AllowAnyMethod()
				.AllowAnyHeader()));
	}

	/// <summary>
	///     Adds one OpenAPI document per host group (bancho/osuweb/beatmapassets/avatar/basilapi).
	/// </summary>
	/// <remarks>
	///     One document per group rather than one for the whole app: routing here is host-based
	///     (<c>RequireHost</c>), and several groups register the same literal path template (both the
	///     bancho and osu-web groups have their own <c>GET /</c>), which OpenAPI cannot represent
	///     twice in one document. Routes opt into a document via <c>.WithGroupName(...)</c>, matching
	///     <c>AddOpenApi</c>'s default <c>ShouldInclude</c> filter.
	/// </remarks>
	/// <param name="builder">The web application builder whose OpenAPI documents are added.</param>
	private static void ConfigureOpenApi(WebApplicationBuilder builder)
	{
		AddOpenApiDocument(builder, "bancho", "osu! Client API: Bancho Protocol",
			"The osu! stable client's binary bancho protocol: login and the packet-multiplexed " +
			"connection that follows it. Served identically from the c./ce./c4./c5./c6. subdomains.");
		AddOpenApiDocument(builder, "osuweb", "osu! Client API: osu! Web",
			"The osu! stable client's HTTP `/web/*.php`-style endpoints (osu!web), plus beatmap/replay " +
			"downloads and in-game registration. Served from the osu. subdomain.");
		AddOpenApiDocument(builder, "beatmapassets", "osu! Client API: Beatmap Assets",
			"Beatmapset thumbnail/preview asset requests, redirected to the api. host's own locally-" +
			"stored background image (self-hosted, no osu.ppy.sh dependency). Served from the b. subdomain.");
		AddOpenApiDocument(builder, "avatar", "osu! Client API: Avatar Files",
			"Locally-stored player avatar images. Served from the a. subdomain.");
		AddOpenApiDocument(builder, "basilapi", "Basil API",
			"Basil's tournament-facing HTTP API: the tournament match report, live SSE channels, " +
			"beatmap/replay file downloads, and admin-key-gated management CRUD. Served from the " +
			"api. subdomain.",
			BasilApiTagGroups);
	}

	/// <summary>
	///     Adds a single OpenAPI document for the given host group with the given title and description.
	/// </summary>
	/// <remarks>
	///     Applies the shared title, description, and version, plus the numeric schema simplification
	///     transformer, to every document. When <paramref name="tagGroups" /> is provided, also writes
	///     <c>document.Tags</c> and the <c>x-tagGroups</c> extension, reorders the documented paths for
	///     Scalar's sidebar (shortest/most general route first per tag section, without touching the
	///     actual route-registration order in Routing/*.cs), and registers the admin-key, converter,
	///     enum-values, polymorphic-one-of, and envelope schema transformers that only apply to the
	///     basilapi document.
	/// </remarks>
	/// <param name="builder">The web application builder whose OpenAPI options are configured.</param>
	/// <param name="documentName">The document name, matching routes' <c>.WithGroupName(...)</c> values.</param>
	/// <param name="title">The document title shown in the generated UI.</param>
	/// <param name="description">The document description shown in the generated UI.</param>
	/// <param name="tagGroups">Optional tag groups driving the Scalar sidebar grouping for the basilapi document.</param>
	private static void AddOpenApiDocument(WebApplicationBuilder builder, string documentName, string title,
		string description, (string Group, (string Tag, string Description)[] Tags)[]? tagGroups = null)
	{
		builder.Services.AddOpenApi(documentName, options =>
		{
			options.AddDocumentTransformer((document, _, _) =>
			{
				document.Info.Title = title;
				document.Info.Description = description;
				document.Info.Version = "v1";

				if (tagGroups is not null)
				{
					document.Tags = tagGroups
						.SelectMany(g => g.Tags)
						.Select(t => new OpenApiTag { Name = t.Tag, Description = t.Description })
						.ToHashSet();

					var tagGroupsJson = new JsonArray(tagGroups.Select(JsonNode (g) =>
						new JsonObject
						{
							["name"] = g.Group,
							["tags"] = new JsonArray(
								g.Tags.Select(t => (JsonNode)t.Tag).ToArray())
						}).ToArray());
					document.Extensions ??= new Dictionary<string, IOpenApiExtension>();
					document.Extensions["x-tagGroups"] = new JsonNodeExtension(tagGroupsJson);

					// Scalar's sidebar buckets each tag's operations by their position in the document,
					// not alphabetically — reorder Paths (shortest/most general route first per tag
					// section) without touching the actual C# route-registration order in Routing/*.cs.
					var reordered = new OpenApiPaths();
					foreach (var (path, item) in document.Paths
						         .OrderBy(kvp => kvp.Key.Count(c => c == '/'))
						         .ThenBy(kvp => kvp.Key.Length)
						         .ThenBy(kvp => kvp.Key, StringComparer.Ordinal))
						reordered[path] = item;
					document.Paths = reordered;
				}

				return Task.CompletedTask;
			});

			// Applies to every document — a generator-default artifact of how .NET's OpenAPI schema
			// builder represents integers/numbers, not specific to basilapi's own types.
			options.AddNumericSchemaSimplificationTransformer();

			// Only the basilapi document is enveloped/admin-key-gated — every other document (bancho/
			// osu-web/beatmap-assets/avatar) documents the raw osu! client protocol as-is.
			if (tagGroups is not null)
			{
				options.AddAdminKeyDocumentTransformer();
				options.AddCustomConverterSchemaTransformer();
				options.AddEnumValuesSchemaTransformer();
				options.AddPolymorphicOneOfSchemaTransformer();
				options.AddEnvelopeSchemaTransformer();
			}
		});
	}

	/// <summary>
	///     Creates the storage folders, runs database migrations, and bootstraps runtime data on startup.
	/// </summary>
	/// <remarks>
	///     Test hosts (<c>WebApplicationFactory</c>) explicitly set <c>Database:Path</c> to "" so there
	///     is no real file to migrate/query against: migration, channel fetch, beatmap ingestion, and
	///     bot bootstrap are skipped rather than failing startup. A real deployment always has a
	///     non-empty <c>Path</c> (<see cref="DatabaseOptions" /> defaults it to "basil.db" next to the
	///     executable). <c>channelRegistry.Seed</c> is still always called (with an empty list when
	///     there is no database), so the registry is never left unseeded.
	/// </remarks>
	/// <param name="app">The built application whose scoped services perform the initialization.</param>
	private static async Task InitializeDataAsync(WebApplication app)
	{
		using var scope = app.Services.CreateScope();
		var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

		var dbOptions = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
		var hasDatabase = !string.IsNullOrEmpty(dbOptions.Path);

		var storageOptions = scope.ServiceProvider.GetRequiredService<IOptions<StorageOptions>>().Value;
		foreach (var path in new[]
		         {
			         storageOptions.ReplaysPath, storageOptions.AvatarsPath, storageOptions.MapsetsPath,
			         storageOptions.SeasonalsPath, storageOptions.FaqsPath
		         })
			Directory.CreateDirectory(path);
		logger.LogInformation("Storage folders ready");

		var channelRepository = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
		var channelRegistry = scope.ServiceProvider.GetRequiredService<IChannelRegistry>();
		IReadOnlyList<Channel> allChannels = [];

		if (hasDatabase)
		{
			var connectionString = DatabaseConnectionStringBuilder.Build(dbOptions);
			Directory.CreateDirectory(Path.GetDirectoryName(DatabaseConnectionStringBuilder.ResolvePath(dbOptions))!);
			logger.LogInformation("Running database migrations");
			SqlMigrationRunner.RunMigrations(connectionString);
			logger.LogInformation("Database migrations complete");

			allChannels = await channelRepository.FetchAllAsync();
		}

		channelRegistry.Seed(allChannels);

		if (hasDatabase)
		{
			var ingestionService = scope.ServiceProvider.GetRequiredService<BeatmapIngestionService>();
			await ingestionService.ReconcileAllAsync();

			var botBootstrap = scope.ServiceProvider.GetRequiredService<BotBootstrapService>();
			await botBootstrap.BootstrapAsync();

			var recoveryService = scope.ServiceProvider.GetRequiredService<MatchRecoveryService>();
			await recoveryService.RecoverAsync();
		}
	}
}
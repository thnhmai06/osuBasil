using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Basil.Application;
using Basil.Application.Abstractions.Channels;
using Basil.Application.Configurations;
using Basil.Application.Formats;
using Basil.Application.Services.Authentication;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions.Channels;
using Basil.Domain.Channels;
using Basil.Infrastructure;
using Basil.Infrastructure.Beatmaps;
using Basil.Infrastructure.Persistence;
using Basil.Web.Auth;
using Basil.Web.Logging;
using Basil.Web.Middleware;
using Basil.Web.OpenApi;
using Basil.Web.Routing.Bancho;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Events;
using ILogger = Microsoft.Extensions.Logging.ILogger;

// ReSharper disable ClassNeverInstantiated.Global

namespace Basil.Web;

public sealed class Program
{
	private const string CorsPolicyName = "ApiCors";

	private const string BasilDescription =
		"A lightweight, high-performance osu! server for tournaments and multiplayer.";

	private const string BasilLicense = "Copyright © 2026 Mai Thành. Licensed under the MIT License.";

	private const string BasilArt =
		"""
		                    _
		                  _(_)_          ____            _wW̲w   _
		      @@@@       (_)@(_)   vVVVv| __ )  __ _ ___(_) |) _(_)_
		     @@()@@ wWWWw  (_)\    (___)|  _ \ / _` / __| | | (_)@(_)
		      @@@@  (___)     `|/    Y  | |_) | (_| \__ \ | |   (_)\
		       /      Y       \|    \|/ |____/ \__,_|___/_|_|      |
		    \ |     \ |/       | / \ | /  \|/       |/    \|      \|/
		    \\|//   \\|///  \\\|//\\\|/// \|///  \\\|//  \\|//  \\\|//
		^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
		"""; // original art by Joan G. Stark (Spunk), edited by Mai Thành (thnhmai06)

	/// <summary>
	///     Defines the tags used by the Basil API documentation, grouped by resource.
	/// </summary>
	/// <remarks>
	///     The declaration order determines how tag groups appear in the generated API documentation.
	/// </remarks>
	private static readonly (string Group, (string Tag, string Description)[] Tags)[] BasilApiTagGroups =
	[
		("Matches",
		[
			("Matches", "Match listing and creation."),
			("Match Report", "Tournament match reports (TRT)."),
			("Match Settings", "Match room configuration."),
			("Match Live", "Room-wide realtime playing status and merged per-slot live streams."),
			("Match Hosts", "Match host management."),
			("Match Referees", "Match referee management."),
			("Match Bans", "Management of players banned from a match."),
			("Match Slots",
				"Management of the match's 16 slots, including assignments, teams, locking, invitations, and kicking players."),
			("Match Timer", "Match countdown timer."),
			("Match Abort", "Abort the match currently in progress."),
			("Match Close", "Close the match immediately."),
			("Match Chat", "The match room's live chat stream, and saying something in it as BasilBot.")
		]),
		("Users",
		[
			("Users", "User CRUD, avatar management, and live spectator input streams.")
		]),
		("Beatmapsets",
		[
			("Beatmapsets", "Beatmapset CRUD, archive and storyboard downloads, and freeze/private management."),
			("Beatmaps", "Individual beatmap lookups and beatmap file/background downloads.")
		]),
		("Scores",
		[
			("Scores", "Score lookups, replay downloads, and paginated score listings.")
		]),
		("FAQ",
		[
			("FAQ", "Public FAQ entries.")
		]),
		("Seasonal Backgrounds",
		[
			("Seasonal Backgrounds", "Public seasonal background images.")
		]),
		("Menu Icon",
		[
			("Menu Icon Image", "The in-game main menu icon image."),
			("Menu Icon URL", "The URL opened when the main menu icon is clicked.")
		]),
		("Admin Key",
		[
			("Admin Key", "The server's admin key: status, rotation, and bypass mode.")
		]),
		("Announce",
		[
			("Announce", "Pushing an in-game notification to online players."),
			("Motd", "The message shown to a player on login.")
		]),
		("Abbreviation Redirects",
		[
			("Abbreviation Redirects", "302 redirects from abbreviated paths to their canonical resource paths.")
		]),
		("Health",
		[
			("Health", "Health and liveness checks.")
		])
	];

	/// <summary>
	///     The host entry point: builds the web application, wires the middleware pipeline and host
	///     groups, initializes startup data, and runs the application.
	/// </summary>
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
		LogStartupBanner(app);

		// Order matters: authentication/authorization must run before EnvelopeMiddleware (which
		// needs the resolved role to decide what a response reveals), which must run before
		// ApiRequestLoggingMiddleware (which logs the final status code).
		app.UseMiddleware<RequestIdLoggingMiddleware>();
		app.UseSerilogRequestLogging();
		app.UseMiddleware<ExceptionLoggingMiddleware>();
		app.UseWebSockets();
		app.UseCors(CorsPolicyName);
		app.UseAuthentication();
		app.UseAuthorization();
		app.UseMiddleware<EnvelopeMiddleware>();
		app.UseMiddleware<ApiRequestLoggingMiddleware>();

		var domain = builder.Configuration.GetSection(ServerOptions.SectionName)["Domain"] ?? "localhost";
		BanchoHostGroups.MapAll(app, domain);

		var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
		var hostLogger = app.Services.GetRequiredService<ILogger<Program>>();
		lifetime.ApplicationStopping.Register(() => hostLogger.LogInformation("Server shutting down"));

		await InitializeDataAsync(app);
		await app.RunAsync();
	}

	/// <summary>Logs the startup banner: ASCII art, version, and host machine/hardware info.</summary>
	/// <param name="app">The built application whose logger is used.</param>
	private static void LogStartupBanner(WebApplication app)
	{
		var logger = app.Services.GetRequiredService<ILogger<Program>>();

		var assembly = typeof(Program).Assembly;
		var version =
			assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
				?.InformationalVersion
			?? assembly.GetName().Version?.ToString()
			?? "unknown";
		var gcInfo = GC.GetGCMemoryInfo();
		var appDrive = new DriveInfo(Path.GetPathRoot(AppContext.BaseDirectory)!);

		LogLine(logger);

		foreach (var line in BasilArt.Split('\n')) logger.LogInformation("{Art}", line.TrimEnd('\r'));
		logger.LogInformation("Basil v{Version}", version);
		logger.LogInformation("{Description}", BasilDescription);
		logger.LogInformation("{License}", BasilLicense);
		logger.LogInformation("Current time (UTC): {Time}", DateTimeOffset.UtcNow.ToString("O"));
		logger.LogInformation(""); // \n

		LogSection(logger, "Environment");
		LogValue(logger, "Machine", Environment.MachineName);
		LogValue(logger, "OS", RuntimeInformation.OSDescription);
		LogValue(logger, "OS Version", Environment.OSVersion.Version);
		LogValue(logger, "OS Architecture", RuntimeInformation.OSArchitecture);
		LogValue(logger, "Runtime", RuntimeInformation.FrameworkDescription);
		LogValue(logger, "Runtime Identifier", RuntimeInformation.RuntimeIdentifier);
		LogValue(logger, "CLR", Environment.Version);
		LogValue(logger, "Process Architecture", RuntimeInformation.ProcessArchitecture);
		LogValue(logger, "Process ID", Environment.ProcessId);
		LogValue(logger, "Process Name",
			Environment.ProcessPath is { } path
				? Path.GetFileNameWithoutExtension(path)
				: "Unknown");
		LogValue(logger, "Logical CPUs", Environment.ProcessorCount);
		LogValue(logger, "64-bit OS", Environment.Is64BitOperatingSystem);
		LogValue(logger, "64-bit Process", Environment.Is64BitProcess);

		LogSection(logger, "GC");
		LogValue(logger, "Server GC", GCSettings.IsServerGC);
		LogValue(logger, "Latency Mode", GCSettings.LatencyMode);
		LogValue(logger, "Memory Limit", $"{gcInfo.TotalAvailableMemoryBytes / 1024d / 1024 / 1024:F2} GiB");
		LogValue(logger, "Heap Size", $"{gcInfo.HeapSizeBytes / 1024d / 1024:F2} MiB");

		LogSection(logger, "Localization");
		LogValue(logger, "Time Zone", TimeZoneInfo.Local.DisplayName);
		LogValue(logger, "Time Zone ID", TimeZoneInfo.Local.Id);
		LogValue(logger, "Culture", CultureInfo.CurrentCulture.Name);
		LogValue(logger, "UI Culture", CultureInfo.CurrentUICulture.Name);

		LogSection(logger, "Storage");
		LogValue(logger, "Application Drive", appDrive.Name);
		LogValue(logger, "Drive Format", appDrive.DriveFormat);
		LogValue(logger, "Total Space", $"{appDrive.TotalSize / 1024d / 1024 / 1024:F2} GiB");
		LogValue(logger, "Free Space", $"{appDrive.AvailableFreeSpace / 1024d / 1024 / 1024:F2} GiB");

		LogSection(logger, "Paths");
		LogValue(logger, "Working Directory", Environment.CurrentDirectory);
		LogValue(logger, "Base Directory", AppContext.BaseDirectory);
		LogValue(logger, "Command Line", Environment.CommandLine);

		LogLine(logger);
		return;

		static void LogLine(ILogger logger)
		{
			logger.LogInformation(
				"────────────────────────────────────────────────────────────────");
		}

		static void LogSection(ILogger logger, string name)
		{
			logger.LogInformation("{Section}:", name);
		}

		static void LogValue(ILogger logger, string key, object? value)
		{
			logger.LogInformation("\t{Key,-22} : {Value}", key, value);
		}
	}

	/// <summary>
	///     Configures Serilog for the host, writing to the console and two daily rolling file sinks.
	/// </summary>
	/// <remarks>
	///     Console and full-file output honor <c>Basil:Logging:MinimumLevel</c> (default
	///     Information); the error file always stays Error-only regardless of that setting.
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
			// worth Information-level noise; only Warning+ from it shows by default. Curated categories,
			// and anything at Warning+ regardless of category, are never touched here.
			.Filter.ByExcluding(e =>
				e.Level < LogEventLevel.Warning &&
				e.Properties.TryGetValue("Category", out var category) &&
				category is ScalarValue { Value: CategoryEnricher.FallbackCategory })
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
	///     The section supplies <c>Port</c> (default 443), <c>CertPath</c>, and <c>CertPassword</c>;
	///     the server binds exclusively on that port. Leaving <c>CertPath</c>/<c>CertPassword</c>
	///     unset uses the dev cert or OS-level TLS. A bad cert path or password is logged at Critical
	///     (path only, never the password) before the process exits with code 1.
	/// </remarks>
	/// <param name="builder">The web application builder whose Kestrel options are configured.</param>
	private static void ConfigureKestrel(WebApplicationBuilder builder)
	{
		builder.WebHost.ConfigureKestrel((context, options) =>
		{
			var logger = options.ApplicationServices.GetService<ILoggerFactory>()?
				.CreateLogger(typeof(Program).FullName ?? "Program");

			var serverSection = context.Configuration.GetSection(ServerOptions.SectionName);
			var domain = serverSection.GetValue<string>("Domain");
			var port = serverSection.GetValue<int?>("Port") ?? 443;
			var rawCertPath = serverSection.GetValue<string?>("CertPath");
			var certPassword = serverSection.GetValue<string?>("CertPassword");

			var certPath = !string.IsNullOrWhiteSpace(rawCertPath)
				? Path.GetFullPath(rawCertPath)
				: null;
			if (certPath is not null) logger?.LogInformation("Certificate path: {CertPath}", certPath);

			try
			{
				if (string.IsNullOrWhiteSpace(domain))
					throw new ValidationException("Domain is required.");
				if (Uri.CheckHostName(domain) != UriHostNameType.Dns)
					throw new ValidationException("Domain must be a valid DNS hostname.");
				if (string.Equals(domain, "localhost", StringComparison.OrdinalIgnoreCase))
					throw new ValidationException("'localhost' is not allowed.");
				if (!domain.Contains('.'))
					throw new ValidationException(
						"Domain must be a fully qualified domain name (for example, 'example.com').");
			}
			catch (ValidationException ex)
			{
				logger?.LogCritical(ex, "Domain is not valid");
				Environment.Exit(1);
			}

			// Lets a browser multiplex several SSE connections over one connection instead of
			// hitting HTTP/1.1's ~6-per-origin ceiling; only takes effect over TLS, which every
			// listener here already uses.
			options.ConfigureEndpointDefaults(listenOptions =>
				listenOptions.Protocols = HttpProtocols.Http1AndHttp2);

			try
			{
				options.ListenAnyIP(port, listenOptions =>
				{
					if (certPath is not null)
						listenOptions.UseHttps(certPath, certPassword);
					else
						listenOptions.UseHttps();
				});
			}
			catch (Exception ex)
			{
				logger?.LogCritical(ex, "Failed to load TLS certificate from {CertPath}.", certPath);
				Environment.Exit(1);
			}
		});
	}

	/// <summary>
	///     Registers the admin-key authentication scheme and the admin-role authorization policy.
	/// </summary>
	/// <remarks>
	///     The scheme reads the <c>Authorization: Bearer</c> header (see
	///     <see cref="AdminKeyAuthenticationHandler" />). One mechanism serves both the hard
	///     admin-only gate (<c>RequireAuthorization</c>) and the soft private/frozen-visibility
	///     elevation (<c>User.IsInRole</c>).
	/// </remarks>
	/// <param name="builder">The web application builder whose authentication services are registered.</param>
	private static void ConfigureAuth(WebApplicationBuilder builder)
	{
		builder.Services
			.AddAuthentication(AdminKeyDefaults.Scheme)
			.AddScheme<AuthenticationSchemeOptions, AdminKeyAuthenticationHandler>(AdminKeyDefaults.Scheme, null);

		builder.Services.AddAuthorizationBuilder()
			.AddPolicy(AdminKeyDefaults.Policy,
				policy => policy.RequireRole(AdminKeyDefaults.Role));
	}

	/// <summary>
	///     Registers <see cref="CountryJsonConverter" /> globally for every JSON response using the host defaults.
	/// </summary>
	/// <remarks>
	///     <see cref="CountryJsonConverter" /> is the only enum with a non-default wire form; every
	///     other enum keeps System.Text.Json's default numeric serialization. Regular JSON responses
	///     and live SSE payloads always agree on this, since both serialize with the same converter set.
	/// </remarks>
	/// <param name="builder">The web application builder whose HTTP JSON options are configured.</param>
	private static void ConfigureJson(WebApplicationBuilder builder)
	{
		// The framework's JsonOptions.SerializerOptions has no public setter, so the instance itself
		// can't be swapped for BasilJsonOptions.Instance (the shared options every live SSE payload
		// serializes with); copying its converters is the closest equivalent.
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
	///     credentials are ever sent (the admin key is a plain <c>Authorization</c> header, not a
	///     cookie), so <c>AllowAnyOrigin</c> is safe here.
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
	///     Adds one OpenAPI document per host group (bancho/osuweb/beatmapasets/avatar/assets/basilapi).
	/// </summary>
	/// <param name="builder">The web application builder whose OpenAPI documents are added.</param>
	private static void ConfigureOpenApi(WebApplicationBuilder builder)
	{
		// One document per group rather than one for the whole app: several groups register the
		// same literal path template (both the bancho and osu-web groups have their own GET /),
		// which OpenAPI cannot represent twice in one document.
		AddOpenApiDocument(
			builder,
			"bancho",
			"osu! Client API: Bancho Protocol",
			"The osu! stable client's binary Bancho protocol, including login and the packet-based session that follows.");
		AddOpenApiDocument(
			builder,
			"osuweb",
			"osu! Client API: osu! Web",
			"The osu! stable client's HTTP `web/*.php` endpoints, along with beatmap and replay downloads and in-game account registration.");
		AddOpenApiDocument(
			builder,
			"beatmapassets",
			"osu! Client API: Beatmap Assets",
			"Beatmapset thumbnail and preview image requests, served from locally stored assets with no dependency on osu.ppy.sh.");
		AddOpenApiDocument(
			builder,
			"avatar",
			"osu! Client API: Avatar Files",
			"Locally hosted user avatar images.");
		AddOpenApiDocument(
			builder,
			"assets",
			"Basil Assets",
			"Menu banner/icon/seasonal images and beatmapset covers, served through ImageSharp.Web.");
		AddOpenApiDocument(
			builder,
			"basilapi",
			"Basil API",
			"Basil's tournament-facing HTTP API, including tournament match reports, live SSE streams, beatmap and replay downloads, and admin-key-protected management endpoints.",
			BasilApiTagGroups);
	}

	/// <summary>
	///     Adds a single OpenAPI document for the given host group with the given title and description.
	/// </summary>
	/// <remarks>
	///     When <paramref name="tagGroups" /> is provided, the document's sidebar is grouped by tag
	///     (the shortest/most general route first per group), and the document is enveloped and
	///     admin-key-gated; every other document describes the raw osu! client protocol as-is.
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

				if (tagGroups is null) return Task.CompletedTask;

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
				// not alphabetically. Reorder Paths (the shortest/most general route first per tag
				// section) without touching the actual C# route-registration order in Routing/*.cs.
				var reordered = new OpenApiPaths();
				foreach (var (path, item) in document.Paths
					         .OrderBy(kvp => kvp.Key.Count(c => c == '/'))
					         .ThenBy(kvp => kvp.Key.Length)
					         .ThenBy(kvp => kvp.Key, StringComparer.Ordinal))
					reordered[path] = item;
				document.Paths = reordered;

				return Task.CompletedTask;
			});

			// Applies to every document: a generator-default artifact of how .NET's OpenAPI schema
			// builder represents integers/numbers, not specific to basilapi's own types.
			options.AddNumericSchemaSimplificationTransformer();

			// Only the basilapi document is enveloped/admin-key-gated. Every other document (bancho/
			// osu-web/beatmap-assets/avatar) documents the raw osu! client protocol as-is.
			if (tagGroups is null) return;

			options.AddAdminKeyDocumentTransformer();
			options.AddCustomConverterSchemaTransformer();
			options.AddEnumValuesSchemaTransformer();
			options.AddPolymorphicOneOfSchemaTransformer();
			options.AddEnvelopeSchemaTransformer();
		});
	}

	/// <summary>
	///     Creates the storage folders, runs database migrations, and bootstraps runtime data on startup.
	/// </summary>
	/// <remarks>
	///     When no database path is configured, migration, channel fetch, beatmap ingestion, and bot
	///     bootstrap are skipped rather than failing startup; the channel registry is still always
	///     seeded, with an empty list in that case.
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

		var mirrorOptions = scope.ServiceProvider.GetRequiredService<IOptions<MirrorOptions>>().Value;
		if (mirrorOptions.IsOnlineMode)
			logger.LogInformation("Beatmap serving mode: ONLINE — Basil:Mirror:DownloadEndpoint is set. " +
			                      "Thumbnails/previews redirect to b.ppy.sh, downloads redirect to the configured " +
			                      "mirror, local-only asset routes report 503, all when missing locally.");
		else
			logger.LogInformation("Beatmap serving mode: OFFLINE — Basil:Mirror:DownloadEndpoint is unset. " +
			                      "All beatmap assets are served from local storage only.");

		var channelRepository = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
		var channelRegistry = scope.ServiceProvider.GetRequiredService<IChannelRegistry>();
		IReadOnlyList<Channel> allChannels = [];

		if (hasDatabase)
		{
			var connectionString = dbOptions.Build();
			Directory.CreateDirectory(Path.GetDirectoryName(dbOptions.ResolvePath())!);
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

			var adminKeyService = scope.ServiceProvider.GetRequiredService<AdminKeyService>();
			if (await adminKeyService.IsBypassAsync())
				logger.LogWarning(
					"NO ADMIN KEY IS CONFIGURED — THE SERVER IS RUNNING IN BYPASS MODE. " +
					"ALL MANAGEMENT ACTIONS AND IN-GAME REGISTRATIONS ARE ACCEPTED WITHOUT AUTHENTICATION. " +
					"THIS IS INSECURE AND SHOULD ONLY BE USED FOR DEVELOPMENT. " +
					"Configure an admin key immediately via PUT /adminkey.");
		}
	}
}
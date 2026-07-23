using System.Net;
using Basil.Application;
using Basil.Application.Abstractions.Channels;
using Basil.Application.Configuration;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions.Channels;
using Basil.Infrastructure.Beatmaps;
using Basil.Infrastructure.DependencyInjection;
using Basil.Infrastructure.Persistence;
using Basil.Web.Auth;
using Basil.Web.Routing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace Basil.Web;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        ConfigureConfiguration(builder, args);
        ConfigureKestrel(builder);
        builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection(ServerOptions.SectionName));

        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddApplication();
        ConfigureOpenApi(builder);
        ConfigureAuth(builder);
        ConfigureCors(builder);

        var app = builder.Build();
        app.UseWebSockets();
        app.UseCors(CorsPolicyName);
        app.UseAuthentication();
        app.UseAuthorization();

        var domain = builder.Configuration.GetSection(ServerOptions.SectionName)["Domain"] ?? "localhost";
        BanchoHostGroups.MapAll(app, domain);

        await InitializeDataAsync(app);
        await app.RunAsync();
    }

    // appsettings.json carries all Basil settings under a "Basil" section alongside standard
    // ASP.NET Core config (Logging, AllowedHosts). appsettings.{env}.json and command-line args
    // are layered on top for environment-specific overrides.
    private static void ConfigureConfiguration(WebApplicationBuilder builder, string[] args)
    {
        builder.Configuration
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddCommandLine(args);
    }

    // Kestrel endpoint and HTTPS cert configured from appsettings.json Basil:Server section (Port,
    // CertPath, CertPassword). Disables auto port selection — server binds exclusively on the
    // configured port. Leave CertPath/CertPassword unset to use the dev cert or OS-level TLS.
    // Http1AndHttp2 lets a browser multiplex several SSE connections over one connection instead of
    // hitting HTTP/1.1's ~6-per-origin ceiling — only takes effect over TLS, which every listener here
    // already uses.
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

            options.Listen(IPAddress.Any, port, listenOptions =>
            {
                if (!string.IsNullOrEmpty(certPath))
                    listenOptions.UseHttps(certPath, certPassword);
                else
                    listenOptions.UseHttps();
            });
        });
    }

    // Custom scheme reading X-Admin-Key (see AdminKeyAuthenticationHandler) instead of the old
    // AdminKeyFilter endpoint filter — lets both the hard admin-only gate (RequireAuthorization) and
    // the soft private/frozen-visibility elevation (User.IsInRole) share one mechanism.
    private static void ConfigureAuth(WebApplicationBuilder builder)
    {
        builder.Services
            .AddAuthentication(AdminKeyDefaults.Scheme)
            .AddScheme<AuthenticationSchemeOptions, AdminKeyAuthenticationHandler>(AdminKeyDefaults.Scheme, null);

        builder.Services.AddAuthorization(options =>
            options.AddPolicy(AdminKeyDefaults.Policy, policy => policy.RequireRole(AdminKeyDefaults.Role)));
    }

    private const string CorsPolicyName = "ApiCors";

    // Permissive by design: the api. host is meant to be called directly from arbitrary browser-based
    // tooling (tournament overlays, dashboards, OBS browser sources). No credentials are ever sent
    // (X-Admin-Key is a plain header, not a cookie), so AllowAnyOrigin is safe here.
    private static void ConfigureCors(WebApplicationBuilder builder)
    {
        builder.Services.AddCors(options =>
            options.AddPolicy(CorsPolicyName, policy => policy
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader()));
    }

    // One OpenAPI document per host group (bancho/osuweb/beatmapassets/avatar/basilapi) rather than
    // one document for the whole app — routing here is host-based (RequireHost), and several groups
    // register the same literal path template (e.g. both the bancho and osu-web groups have their
    // own GET /), which OpenAPI can't represent twice in one document. Routes opt into a document via
    // .WithGroupName(...), matching AddOpenApi's default ShouldInclude filter.
    private static void ConfigureOpenApi(WebApplicationBuilder builder)
    {
        AddOpenApiDocument(builder, "bancho", "osu! Client API — Bancho Protocol",
            "The osu! stable client's binary bancho protocol — login and the packet-multiplexed " +
            "connection that follows it. Served identically from the c./ce./c4./c5./c6. subdomains.");
        AddOpenApiDocument(builder, "osuweb", "osu! Client API — osu! Web",
            "The osu! stable client's HTTP `/web/*.php`-style endpoints (osu!web), plus beatmap/replay " +
            "downloads and in-game registration. Served from the osu. subdomain.");
        AddOpenApiDocument(builder, "beatmapassets", "osu! Client API — Beatmap Assets",
            "Legacy beatmap thumbnail/preview asset requests, redirected to osu.ppy.sh's own CDN. " +
            "Served from the b. subdomain.");
        AddOpenApiDocument(builder, "avatar", "osu! Client API — Avatar Files",
            "Locally-stored player avatar images. Served from the a. subdomain.");
        AddOpenApiDocument(builder, "basilapi", "Basil API",
            "Basil's tournament-facing HTTP API: the tournament match report, live SSE channels, " +
            "beatmap/replay file downloads, and admin-key-gated management CRUD. Served from the " +
            "api. subdomain.");
    }

    private static void AddOpenApiDocument(WebApplicationBuilder builder, string documentName, string title,
        string description)
    {
        builder.Services.AddOpenApi(documentName, options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = title;
                document.Info.Description = description;
                document.Info.Version = "v1";
                return Task.CompletedTask;
            });
        });
    }

    // Test hosts (WebApplicationFactory) explicitly set Database:Path to "" so there's no real file
    // to migrate/query against — skip migration, channel fetch, ingestion, and bot bootstrap rather
    // than fail startup. A real deployment always has a non-empty Path (DatabaseOptions defaults it
    // to "basil.db" next to the executable). channelRegistry.Seed is still always called (with an
    // empty list when there's no database), so the registry is never left unseeded.
    private static async Task InitializeDataAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var dbOptions = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var hasDatabase = !string.IsNullOrEmpty(dbOptions.Path);

        var storageOptions = scope.ServiceProvider.GetRequiredService<IOptions<StorageOptions>>().Value;
        foreach (var path in new[]
                 {
                     storageOptions.ReplaysPath, storageOptions.AvatarsPath, storageOptions.MapsetsPath,
                     storageOptions.SeasonalsPath, storageOptions.FaqsPath
                 })
            Directory.CreateDirectory(path);

        var channelRepository = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
        var channelRegistry = scope.ServiceProvider.GetRequiredService<IChannelRegistry>();
        IReadOnlyList<Channel> autoJoinChannels = Array.Empty<Channel>();

        if (hasDatabase)
        {
            var connectionString = DatabaseConnectionStringBuilder.Build(dbOptions);
            Directory.CreateDirectory(Path.GetDirectoryName(DatabaseConnectionStringBuilder.ResolvePath(dbOptions))!);
            SqlMigrationRunner.RunMigrations(connectionString);

            autoJoinChannels = await channelRepository.FetchAllAutoJoinAsync();
        }

        channelRegistry.Seed(autoJoinChannels);

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
using Basil.Application.Abstractions.Channels;
using Basil.Application.Configuration;
using Basil.Application.DependencyInjection;
using Basil.Application.Sessions.Channels;
using Basil.Application.UseCases.Bot;
using Basil.Application.UseCases.Multiplayer;
using Basil.Infrastructure.Beatmaps;
using Basil.Infrastructure.DependencyInjection;
using Basil.Infrastructure.Persistence;
using Basil.Web.Routing;
using Microsoft.Extensions.Options;
using System.Net;
using Tomlyn.Extensions.Configuration;

namespace Basil.Web;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        ConfigureConfiguration(builder, args);
        ConfigureKestrel(builder);
        builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection(ServerOptions.SectionName));

        builder.Services.AddBanchoInfrastructure(builder.Configuration);
        builder.Services.AddBanchoApplication();

        var app = builder.Build();
        app.UseWebSockets();

        var domain = builder.Configuration.GetSection(ServerOptions.SectionName)["Domain"] ?? "localhost";
        BanchoHostGroups.MapAll(app, domain);

        await InitializeDataAsync(app);
        await app.RunAsync();
    }

    // appsettings*.json keeps framework config (Logging, AllowedHosts) — standard ASP.NET Core
    // convention, untouched. Settings.toml carries all Basil settings (Server, Mirror, Bot, Api,
    // Database) — same file for development and deployment, edit directly next to the executable,
    // no rebuild needed. Settings.toml is the single source of truth, no env-var override layer.
    private static void ConfigureConfiguration(WebApplicationBuilder builder, string[] args)
    {
        builder.Configuration.Sources.Clear();
        builder.Configuration
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddTomlFile("Settings.toml", optional: false, reloadOnChange: true)
            .AddCommandLine(args);
    }

    // Kestrel endpoint and HTTPS cert configured from Settings.toml [Server] section (Port,
    // CertPath, CertPassword). Disables auto port selection — server binds exclusively on the
    // configured port. Leave CertPath/CertPassword unset to use the dev cert or OS-level TLS.
    private static void ConfigureKestrel(WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel((context, options) =>
        {
            var serverSection = context.Configuration.GetSection(ServerOptions.SectionName);
            var port = serverSection.GetValue<int?>("Port") ?? 443;
            var certPath = serverSection["CertPath"];
            var certPassword = serverSection["CertPassword"];

            options.Listen(IPAddress.Any, port, listenOptions =>
            {
                if (!string.IsNullOrEmpty(certPath))
                    listenOptions.UseHttps(certPath, certPassword);
                else
                    listenOptions.UseHttps();
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
            await ingestionService.IngestAsync();

            var botBootstrap = scope.ServiceProvider.GetRequiredService<BotBootstrapService>();
            await botBootstrap.BootstrapAsync();

            var recoveryService = scope.ServiceProvider.GetRequiredService<MatchRecoveryService>();
            await recoveryService.RecoverAsync();
        }
    }
}
using Basil.Application.Abstractions.Channels;
using Basil.Application.Configuration;
using Basil.Application.DependencyInjection;
using Basil.Application.Sessions.Channels;
using Basil.Application.UseCases.Bot;
using Basil.Infrastructure.Beatmaps;
using Basil.Infrastructure.DependencyInjection;
using Basil.Infrastructure.Persistence;
using Basil.Web.Routing;
using Microsoft.Extensions.Options;
using Tomlyn.Extensions.Configuration;

namespace Basil.Web;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        ConfigureConfiguration(builder, args);
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
    // convention, untouched. settings.toml carries Basil's own server settings (Server,
    // Mirror, Bot, Api, Database) — the same file for development and deployment, edit it directly
    // next to the executable, no rebuild needed. No general environment-variable override layer
    // for those — settings.toml is the single source of truth for them. The one exception is
    // Kestrel:* (HTTPS cert path/password): unlike the app's own settings, a cert password
    // shouldn't sit in plaintext in a config file, so it stays env-var-only. Read manually rather
    // than via AddEnvironmentVariables(prefix) — that overload strips the prefix itself, which
    // would drop the "Kestrel" section name Kestrel's own options binding needs.
    private static void ConfigureConfiguration(WebApplicationBuilder builder, string[] args)
    {
        var kestrelEnvVars = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Where(entry => ((string)entry.Key).StartsWith("Kestrel__", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => ((string)entry.Key).Replace("__", ":"), entry => (string?)entry.Value);

        builder.Configuration.Sources.Clear();
        builder.Configuration
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddTomlFile("settings.toml", optional: false, reloadOnChange: true)
            .AddInMemoryCollection(kestrelEnvVars)
            .AddCommandLine(args);
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
        }
    }
}
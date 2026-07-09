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

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ServerBehaviorOptions>(
    builder.Configuration.GetSection(ServerBehaviorOptions.SectionName));

builder.Services.AddBanchoInfrastructure(builder.Configuration);
builder.Services.AddBanchoApplication();

var app = builder.Build();

app.UseWebSockets();

var domain = builder.Configuration.GetSection(ServerBehaviorOptions.SectionName)["Domain"] ?? "localhost";

BanchoHostGroups.MapAll(app, domain);

using (var scope = app.Services.CreateScope())
{
    // Test hosts (WebApplicationFactory) explicitly set Database:Path to "" so there's no real file
    // to migrate/query against — skip migration, channel fetch, ingestion, and bot bootstrap rather
    // than fail startup. A real deployment always has a non-empty Path (DatabaseOptions defaults it
    // to "basil.db" next to the executable). channelRegistry.Seed is still always called (with an
    // empty list when there's no database) so the registry is never left un-seeded.
    var dbOptions = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    var hasDatabase = !string.IsNullOrEmpty(dbOptions.Path);

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

app.Run();

public partial class Program;

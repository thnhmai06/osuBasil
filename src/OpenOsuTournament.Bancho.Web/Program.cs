using Microsoft.Extensions.Options;
using OpenOsuTournament.Bancho.Application.Abstractions.Channels;
using OpenOsuTournament.Bancho.Application.Configuration;
using OpenOsuTournament.Bancho.Application.DependencyInjection;
using OpenOsuTournament.Bancho.Application.Sessions.Channels;
using OpenOsuTournament.Bancho.Application.UseCases.Bot;
using OpenOsuTournament.Bancho.Infrastructure.Beatmaps;
using OpenOsuTournament.Bancho.Infrastructure.DependencyInjection;
using OpenOsuTournament.Bancho.Infrastructure.Persistence;
using OpenOsuTournament.Bancho.Web.Routing;

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
    // Test hosts (WebApplicationFactory) don't configure a Database section, so Host is empty
    // there and there's nothing to migrate against — skip rather than fail startup.
    var dbOptions = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    if (!string.IsNullOrEmpty(dbOptions.Host))
        SqlMigrationRunner.RunMigrations(DatabaseConnectionStringBuilder.Build(dbOptions));

    var channelRepository = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
    var channelRegistry = scope.ServiceProvider.GetRequiredService<IChannelRegistry>();
    channelRegistry.Seed(await channelRepository.FetchAllAutoJoinAsync());

    // Same test-host guard as the migration above: no Database section means no DB to write into.
    if (!string.IsNullOrEmpty(dbOptions.Host))
    {
        var ingestionService = scope.ServiceProvider.GetRequiredService<BeatmapIngestionService>();
        await ingestionService.IngestAsync();

        var botBootstrap = scope.ServiceProvider.GetRequiredService<BotBootstrapService>();
        await botBootstrap.BootstrapAsync();
    }
}

app.Run();

public partial class Program;
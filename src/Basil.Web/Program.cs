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
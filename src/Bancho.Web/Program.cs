using Bancho.Application.Abstractions;
using Bancho.Application.Configuration;
using Bancho.Application.DependencyInjection;
using Bancho.Application.Sessions;
using Bancho.Infrastructure.DependencyInjection;
using Bancho.Infrastructure.Persistence;
using Bancho.Web.Routing;
using Microsoft.Extensions.Options;
using Bancho.Application.Abstractions.Channels;
using Bancho.Application.Sessions.Channels;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ServerBehaviorOptions>(
    builder.Configuration.GetSection(ServerBehaviorOptions.SectionName));

builder.Services.AddBanchoInfrastructure(builder.Configuration);
builder.Services.AddBanchoApplication();

var app = builder.Build();

var domain = builder.Configuration.GetSection(ServerBehaviorOptions.SectionName)["Domain"] ?? "localhost";

BanchoHostGroups.MapAll(app, domain);

using (var scope = app.Services.CreateScope())
{
    // Test hosts (WebApplicationFactory) don't configure a Database section, so Host is empty
    // there and there's nothing to migrate against — skip rather than fail startup.
    var dbOptions = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    if (!string.IsNullOrEmpty(dbOptions.Host))
    {
        SqlMigrationRunner.RunMigrations(DatabaseConnectionStringBuilder.Build(dbOptions));
    }

    var channelRepository = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
    var channelRegistry = scope.ServiceProvider.GetRequiredService<IChannelRegistry>();
    channelRegistry.Seed(await channelRepository.FetchAllAutoJoinAsync());
}

app.Run();

public partial class Program;

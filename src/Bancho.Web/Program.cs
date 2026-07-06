using Bancho.Application.Abstractions;
using Bancho.Application.Configuration;
using Bancho.Application.DependencyInjection;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Infrastructure.DependencyInjection;
using Bancho.Infrastructure.Persistence;
using Bancho.Web.Routing;
using Microsoft.Extensions.Options;

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

        // Ported from initialize_ram_caches: BanchoBot gets a real, permanent session (never
        // ghost-disconnected — LoginTime/LastRecvTime are pinned far in the future) so command
        // responses and PM auto-replies have somewhere to originate from. Priv is UNRESTRICTED
        // only (not Verified), matching bancho.py's bot construction exactly.
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var botUser = await userRepository.FetchByIdAsync(1);
        if (botUser is not null)
        {
            var tokenGenerator = scope.ServiceProvider.GetRequiredService<ITokenGenerator>();
            var sessionRegistry = scope.ServiceProvider.GetRequiredService<IPlayerSessionRegistry>();
            var botSession = new PlayerSession(
                botUser.Id, botUser.Name, tokenGenerator.GenerateToken(), Privileges.Unrestricted,
                loginTime: int.MaxValue, isBotClient: true);
            sessionRegistry.Add(botSession);
        }
    }

    var channelRepository = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
    var channelRegistry = scope.ServiceProvider.GetRequiredService<IChannelRegistry>();
    channelRegistry.Seed(await channelRepository.FetchAllAutoJoinAsync());
}

app.Run();

public partial class Program;

using Basil.Application;
using Basil.Infrastructure;
using Basil.Server.Shared.Configuration;
using Basil.Server.Shared.Http;

namespace Basil.Server.Host;

/// <summary>The one place that names every slice, so adding a slice is a two-line change here.</summary>
internal static class SliceRegistration
{
	/// <summary>Registers every slice's services with the container.</summary>
	/// <param name="builder">The web application builder whose service collection is populated.</param>
	public static void AddAll(WebApplicationBuilder builder)
	{
		builder.Services.AddInfrastructure(builder.Configuration);
		builder.Services.AddApplication();
	}

	/// <summary>Maps every slice's routes onto the host's Bancho host groups.</summary>
	/// <param name="app">The built application whose routes are mapped.</param>
	public static void MapAll(WebApplication app)
	{
		var domain = app.Configuration.GetSection(ServerOptions.SectionName)["Domain"] ?? "localhost";
		BanchoHostGroups.MapAll(app, domain);
	}
}

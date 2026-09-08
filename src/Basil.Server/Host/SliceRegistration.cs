using Basil.Server.Features.Auth;
using Basil.Server.Features.Beatmaps;
using Basil.Server.Features.Bot;
using Basil.Server.Features.Chat;
using Basil.Server.Features.Content;
using Basil.Server.Features.Irc;
using Basil.Server.Features.Multiplayer;
using Basil.Server.Features.Scores;
using Basil.Server.Features.Spectating;
using Basil.Server.Features.Users;
using Basil.Server.Shared.Configuration;
using Basil.Server.Shared.Http;

namespace Basil.Server.Host;

/// <summary>The one place that names every slice, so adding a slice is a two-line change here.</summary>
internal static class SliceRegistration
{
	/// <summary>Registers every slice's services with the container.</summary>
	/// <remarks>
	///     <c>AddSharedInfrastructure</c> runs first, so every slice's registrations can assume the
	///     shared database/storage options and the shared memory cache are already available.
	/// </remarks>
	/// <param name="builder">The web application builder whose service collection is populated.</param>
	public static void AddAll(WebApplicationBuilder builder)
	{
		builder.Services.AddSharedInfrastructure(builder.Configuration);
		builder.Services.AddAuth(builder.Configuration);
		builder.Services.AddUsers(builder.Configuration);
		builder.Services.AddChat(builder.Configuration);
		builder.Services.AddBot(builder.Configuration);
		builder.Services.AddIrc(builder.Configuration);
		builder.Services.AddMultiplayer(builder.Configuration);
		builder.Services.AddBeatmaps(builder.Configuration);
		builder.Services.AddScores(builder.Configuration);
		builder.Services.AddSpectating(builder.Configuration);
		builder.Services.AddContent(builder.Configuration);
	}

	/// <summary>Maps every slice's routes onto the host's Bancho host groups.</summary>
	/// <remarks>
	///     The `api.` host's route order changes slightly from the pre-split layout: the abbreviation
	///     redirects (mapped inside <see cref="ApiHostRoutes.MapApiGroup" />, which now runs first)
	///     register before the slice-owned routes instead of after. Route templates don't overlap
	///     between the two, so this has no observable effect on route matching.
	/// </remarks>
	/// <param name="app">The built application whose routes are mapped.</param>
	public static void MapAll(WebApplication app)
	{
		var domain = app.Configuration.GetSection(ServerOptions.SectionName)["Domain"] ?? "localhost";
		var hosts = BanchoHostGroups.Create(app, domain);

		hosts.Bancho.MapBanchoGroup();
		hosts.OsuWeb.MapOsuWebGroup();
		hosts.BeatmapAssets.MapBeatmapAssetGroup();
		hosts.Avatar.MapAvatarGroup();

		hosts.Api.MapApiGroup();
		hosts.Api.MapMultiplayerRoutes();
		hosts.Api.MapUsersRoutes();
		hosts.Api.MapScoresRoutes();
		hosts.Api.MapBeatmapsRoutes();
		hosts.Api.MapContentRoutes();
		hosts.Api.MapAuthRoutes();

		hosts.Assets.MapAssetsGroup();
		hosts.Assets.MapMenuAssetRoutes();
		hosts.Assets.MapBeatmapsetAssetRoutes();
	}
}
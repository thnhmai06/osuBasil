using Basil.Application.Beatmaps.Messages;
using Basil.Application.Rulesets.Messages;
using Basil.Application.Rulesets.Objects;
using Basil.Application.Rulesets.Services;
using Basil.Host.Rest.Http;
using Mediator;

namespace Basil.Host.Rest.Routes;

internal static class RulesetRoutes
{
	public static void MapRulesets(this RouteGroupBuilder v1)
	{
		v1.MapGet("/version", VersionAsync).WithTags("Version")
			.WithSummary("Get the application and ruleset versions.");

		var ruleset = v1.MapGroup("/settings/ruleset").WithTags("Settings").RequireToken();
		ruleset.MapGet("/version", RulesetVersionAsync)
			.WithSummary("Get the state of the osu! calculation stack.")
			.WithDescription("Reports the version the server needs and whether it is `installed`, `missing` or `damaged`. Without it, star ratings and pp stay empty and beatmapset imports are refused.");
		ruleset.MapPost("/update", UpdateAsync)
			.WithSummary("Install the osu! calculation stack.")
			.WithDescription("""
			                 Downloads the files the server needs for star ratings, pp and beatmapset imports, and keeps them only when every file checks out. When the stack is already installed, `downloaded` is `false` and nothing is done.

			                 Right after a first installation the calculations start on their own and the beatmapset files waiting to be imported are taken in. Star ratings missing from stored beatmaps are calculated in the background; recalculate the pp of stored scores with `POST /v1/scores/performance/recalculate`. When the stack was damaged while the server was running, `restartRequired` is `true`: restart the server to use the repaired files.

			                 Another installation in progress is refused with 409. An unreachable feed, or a package that fails its check, is answered with 502 and names the package.
			                 """);

		v1.MapPost("/beatmapsets/reload", ReloadAsync).RequireToken().WithTags("Beatmapsets")
			.WithSummary("Update the stored beatmaps.")
			.WithDescription("""
			                 Recomputes every stored beatmap, star rating included, with the rulesets in use. The call answers 202 at once and the work goes on; only one reload runs at a time, and a second request is refused with 409.
			                 """);
	}

	private static IResult VersionAsync(IRulesetUpdater rulesets)
	{
		return Api.Ok(new VersionBody(AppVersion.Current, rulesets.Version().Version, rulesets.Version().State));
	}

	private static async Task<IResult> RulesetVersionAsync(HttpContext context, ISender sender)
	{
		var (result, versions) = await sender.Send(new GetRulesetVersion(context.Session()), context.RequestAborted);
		return result == RulesetUpdateResult.Ok ? Api.Ok(versions!) : result.Refusal();
	}

	private static async Task<IResult> UpdateAsync(HttpContext context, ISender sender)
	{
		try
		{
			var (result, install) = await sender.Send(new UpdateRulesets(context.Session()), context.RequestAborted);
			return result == RulesetUpdateResult.Ok
				? Api.Ok(new UpdateBody(install!.Version, install.Downloaded, install.RestartRequired))
				: result.Refusal();
		}
		catch (RulesetUpdateException e)
		{
			return Api.Error(StatusCodes.Status502BadGateway, "rulesetUpdateFailed", e.Message);
		}
	}

	private static async Task<IResult> ReloadAsync(HttpContext context, ISender sender)
	{
		var result = await sender.Send(new ReloadBeatmaps(context.Session()), context.RequestAborted);
		return result == RulesetUpdateResult.Ok ? Api.Accepted("Reload started") : result.Refusal();
	}

	internal sealed record VersionBody(string App, string Ruleset, RulesetState RulesetState);

	internal sealed record UpdateBody(string Version, bool Downloaded, bool RestartRequired);
}
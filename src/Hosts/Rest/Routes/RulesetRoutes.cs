using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Services;
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
			.WithSummary("Get the ruleset versions.")
			.WithDescription("Reports the active version and the one waiting for a restart.");
		ruleset.MapPost("/update", UpdateAsync)
			.WithSummary("Update the ruleset version.")
			.WithDescription("""
			                 Stages the newest stable version for the next restart, after checking it against the hash its feed publishes; nothing is kept when the check fails. The star ratings already stored stay as they were until the beatmaps are reloaded with `POST /v1/beatmapsets/reload`.

			                 When no newer version exists, `staged` is `false` and nothing needs to be done.

			                 An unreachable feed, or a package that fails the check, is answered with 502.
			                 """);

		v1.MapPost("/beatmapsets/reload", ReloadAsync).RequireToken().WithTags("Beatmapsets")
			.WithSummary("Update the stored beatmaps.")
			.WithDescription("""
			                 Recomputes every stored beatmap, star rating included, with the rulesets in use. The call answers 202 at once and the work goes on; only one reload runs at a time, and a second request is refused with 409.
			                 """);
	}

	private static IResult VersionAsync(IRulesetUpdater rulesets)
	{
		return Api.Ok(new VersionBody(AppVersion.Current, rulesets.Version().Active));
	}

	private static async Task<IResult> RulesetVersionAsync(HttpContext context, ISender sender)
	{
		var (result, versions) = await sender.Send(new GetRulesetVersion(context.Connection()), context.RequestAborted);
		return result == RulesetOutcome.Ok ? Api.Ok(versions!) : result.Refusal();
	}

	private static async Task<IResult> UpdateAsync(HttpContext context, ISender sender)
	{
		try
		{
			var (result, stage) = await sender.Send(new UpdateRulesets(context.Connection()), context.RequestAborted);
			return result == RulesetOutcome.Ok
				? Api.Ok(new UpdateBody(stage!.Version, stage.Staged, stage.Staged, stage.Staged))
				: result.Refusal();
		}
		catch (RulesetUpdateException e)
		{
			return Api.Error(StatusCodes.Status502BadGateway, "rulesetUpdateFailed", e.Message);
		}
	}

	private static async Task<IResult> ReloadAsync(HttpContext context, ISender sender)
	{
		var result = await sender.Send(new ReloadBeatmaps(context.Connection()), context.RequestAborted);
		return result == RulesetOutcome.Ok ? Api.Accepted("Reload started") : result.Refusal();
	}

	internal sealed record VersionBody(string App, string Ruleset);

	internal sealed record UpdateBody(string Version, bool Staged, bool RestartRequired, bool ReloadRequired);
}
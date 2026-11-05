using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Services;
using Basil.Host.Rest.Http;
using Basil.Host.Shared;
using Mediator;

namespace Basil.Host.Rest.Routes;

/// <summary>The application version, the osu! rulesets that compute star ratings, and reloading beatmaps with them.</summary>
internal static class RulesetRoutes
{
	public static void MapRulesets(this RouteGroupBuilder v1)
	{
		v1.MapGet("/version", VersionAsync).WithTags("Version")
			.WithSummary("Retrieve the application version and the active ruleset version.");

		var ruleset = v1.MapGroup("/settings/ruleset").WithTags("Settings").RequireToken();
		ruleset.MapGet("/version", RulesetVersionAsync)
			.WithSummary("Retrieve the active ruleset version and the version waiting for a restart.");
		ruleset.MapPost("/update", UpdateAsync)
			.WithSummary("Download the newest ruleset version and stage it for the next restart.")
			.WithDescription("""
			                 The newest stable version is downloaded and checked against the hash its feed publishes; nothing is kept when the check fails. A staged version takes effect after the server restarts, and the star ratings already stored stay as they were until beatmaps are reloaded.

			                 When no newer version exists, `staged` is `false` and nothing needs to be done.

			                 Related: `POST /v1/beatmapsets/reload` recomputes the stored beatmaps. An unreachable feed or a package that fails the check is answered with 502.
			                 """);

		v1.MapPost("/beatmapsets/reload", ReloadAsync).RequireToken().WithTags("Beatmapsets")
			.WithSummary("Recompute the metadata of every beatmap with the rulesets in use.")
			.WithDescription("""
			                 Recomputes every stored beatmap, star rating included. It runs in the background and the call answers 202 at once; only one reload runs at a time and a second request is refused with 409.
			                 """);
	}

	private static IResult VersionAsync(IRulesetUpdater rulesets)
	{
		return Api.Ok(new VersionBody(AppVersion.Current, rulesets.Version().Active));
	}

	private static async Task<IResult> RulesetVersionAsync(HttpContext context, ISender sender)
	{
		var (result, versions) = await sender.Send(new GetRulesetVersion(context.Connection()), context.RequestAborted);
		return result.IsOk ? Api.Ok(versions!) : result.Refusal();
	}

	private static async Task<IResult> UpdateAsync(HttpContext context, ISender sender)
	{
		try
		{
			var (result, stage) = await sender.Send(new UpdateRulesets(context.Connection()), context.RequestAborted);
			return result.IsOk
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
		return result.IsOk ? Api.Accepted("Reload started") : result.Refusal();
	}

	/// <summary>The versions in use.</summary>
	/// <param name="App">The version of the application.</param>
	/// <param name="Ruleset">The version of the active rulesets.</param>
	internal sealed record VersionBody(string App, string Ruleset);

	/// <summary>The result of staging the newest rulesets.</summary>
	/// <param name="Version">The newest stable version.</param>
	/// <param name="Staged">Whether this call staged it.</param>
	/// <param name="RestartRequired">Whether the server must restart for the staged version to take effect.</param>
	/// <param name="ReloadRequired">Whether the stored beatmaps must be reloaded after that restart.</param>
	internal sealed record UpdateBody(string Version, bool Staged, bool RestartRequired, bool ReloadRequired);
}

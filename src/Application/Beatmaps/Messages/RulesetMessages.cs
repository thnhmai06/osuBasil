using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Services;
using Basil.Application.Common;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

/// <summary>Asks Beatmaps for the active and the staged ruleset version on behalf of an actor.</summary>
/// <param name="Actor">The actor acting.</param>
public sealed record GetRulesetVersion(IActor Actor)
	: IRequest<(Result<RulesetOutcome> Result, RulesetVersions? Versions)>;

/// <summary>Asks Beatmaps to download and stage the newest ruleset version on behalf of an actor.</summary>
/// <param name="Actor">The actor acting.</param>
/// <remarks>
///     The request fails with <see cref="RulesetUpdateException" /> when the feed cannot be read or a package fails
///     verification.
/// </remarks>
public sealed record UpdateRulesets(IActor Actor)
	: IRequest<(Result<RulesetOutcome> Result, RulesetStageResult? Stage)>;

/// <summary>Asks Beatmaps to start recomputing every stored beatmap on behalf of an actor.</summary>
/// <param name="Actor">The actor acting.</param>
public sealed record ReloadBeatmaps(IActor Actor) : IRequest<Result<RulesetOutcome>>;
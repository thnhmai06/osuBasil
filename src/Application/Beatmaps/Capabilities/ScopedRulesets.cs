using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Services;
using Basil.Application.Common;
using Basil.Domain.Users;

namespace Basil.Application.Beatmaps.Capabilities;

/// <summary>The rulesets as one actor may use them.</summary>
/// <remarks>Every operation requires <see cref="UserPermissions.ServerManageSettings" />.</remarks>
public readonly struct ScopedRulesets
{
	private readonly Rulesets _rulesets;
	private readonly UserPermissions _rights;

	internal ScopedRulesets(Rulesets rulesets, IActor actor)
	{
		_rulesets = rulesets;
		_rights = actor.EffectivePermissions(rulesets.Clock.GetUtcNow());
	}

	private UserPermissions? Require()
	{
		return ScopedRights.Require(_rights, UserPermissions.ServerManageSettings);
	}

	/// <summary>Reads the active and the staged version.</summary>
	/// <returns>The outcome and, when allowed, the versions.</returns>
	public (Result<RulesetOutcome> Result, RulesetVersions? Versions) Version()
	{
		return Require() is { } missing
			? (Result<RulesetOutcome>.Denied(missing), null)
			: (RulesetOutcome.Ok, _rulesets.Updater.Version());
	}

	/// <summary>Downloads and stages the newest version; it takes effect after a restart.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome and, when allowed, what was staged.</returns>
	/// <exception cref="RulesetUpdateException">The feed could not be read or a package failed verification.</exception>
	public async Task<(Result<RulesetOutcome> Result, RulesetStageResult? Stage)> UpdateAsync(
		CancellationToken cancellationToken = default)
	{
		return Require() is { } missing
			? (Result<RulesetOutcome>.Denied(missing), null)
			: (RulesetOutcome.Ok, await _rulesets.Updater.StageAsync(cancellationToken));
	}

	/// <summary>Starts recomputing every stored beatmap with the rulesets in use, in the background.</summary>
	/// <returns>Ok when it started; <see cref="RulesetOutcome.Busy" /> when a reload is already running.</returns>
	public Result<RulesetOutcome> Reload()
	{
		return Require() is { } missing
			? Result<RulesetOutcome>.Denied(missing)
			: _rulesets.Reload();
	}
}
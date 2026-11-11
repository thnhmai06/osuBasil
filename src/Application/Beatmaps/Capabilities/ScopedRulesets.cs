using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Services;
using Basil.Application.Common;
using Basil.Domain.Users;

namespace Basil.Application.Beatmaps.Capabilities;

/// <summary>The rulesets as one actor may use them.</summary>
/// <remarks>Every operation requires <see cref="UserPrivileges.Developer" />.</remarks>
public readonly struct ScopedRulesets
{
	private readonly Rulesets _rulesets;
	private readonly UserPrivileges _privileges;

	internal ScopedRulesets(Rulesets rulesets, IActor actor)
	{
		_rulesets = rulesets;
		_privileges = actor.EffectivePrivileges(rulesets.Clock.GetUtcNow());
	}

	private bool Lacks()
	{
		return ScopedPrivileges.Lacks(_privileges, UserPrivileges.Developer);
	}

	/// <summary>Reads the active and the staged version.</summary>
	/// <returns>The outcome and, when allowed, the versions.</returns>
	public (RulesetOutcome Result, RulesetVersions? Versions) Version()
	{
		return Lacks()
			? (RulesetOutcome.Denied, null)
			: (RulesetOutcome.Ok, _rulesets.Updater.Version());
	}

	/// <summary>Downloads and stages the newest version; it takes effect after a restart.</summary>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The outcome and, when allowed, what was staged.</returns>
	/// <exception cref="RulesetUpdateException">The feed could not be read or a package failed verification.</exception>
	public async Task<(RulesetOutcome Result, RulesetStageResult? Stage)> UpdateAsync(
		CancellationToken ct = default)
	{
		return Lacks()
			? (RulesetOutcome.Denied, null)
			: (RulesetOutcome.Ok, await _rulesets.Updater.StageAsync(ct));
	}

	/// <summary>Starts recomputing every stored beatmap with the rulesets in use, in the background.</summary>
	/// <returns>Ok when it started; <see cref="RulesetOutcome.Busy" /> when a reload is already running.</returns>
	public RulesetOutcome Reload()
	{
		return Lacks()
			? RulesetOutcome.Denied
			: _rulesets.Reload();
	}
}
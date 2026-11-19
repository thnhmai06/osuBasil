using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Services;
using Basil.Application.Shared.Actors;
using Basil.Application.Shared.Privileges;
using Basil.Domain.Users;

namespace Basil.Application.Beatmaps.Capabilities;

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

	public (RulesetUpdateResult Result, RulesetVersions? Versions) Version()
	{
		return Lacks()
			? (RulesetUpdateResult.Denied, null)
			: (RulesetUpdateResult.Ok, _rulesets.Updater.Version());
	}

	public async Task<(RulesetUpdateResult Result, RulesetStageResult? Stage)> UpdateAsync(
		CancellationToken ct = default)
	{
		return Lacks()
			? (RulesetUpdateResult.Denied, null)
			: (RulesetUpdateResult.Ok, await _rulesets.Updater.StageAsync(ct));
	}

	public RulesetUpdateResult Reload()
	{
		return Lacks()
			? RulesetUpdateResult.Denied
			: _rulesets.Reload();
	}
}
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Services;
using Basil.Application.Shared.Actors;
using Basil.Application.Shared.Privileges;
using Basil.Domain.Game.Users;

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

	public (RulesetOutcome Result, RulesetVersions? Versions) Version()
	{
		return Lacks()
			? (RulesetOutcome.Denied, null)
			: (RulesetOutcome.Ok, _rulesets.Updater.Version());
	}

	public async Task<(RulesetOutcome Result, RulesetStageResult? Stage)> UpdateAsync(
		CancellationToken ct = default)
	{
		return Lacks()
			? (RulesetOutcome.Denied, null)
			: (RulesetOutcome.Ok, await _rulesets.Updater.StageAsync(ct));
	}

	public RulesetOutcome Reload()
	{
		return Lacks()
			? RulesetOutcome.Denied
			: _rulesets.Reload();
	}
}
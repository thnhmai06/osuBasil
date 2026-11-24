using Basil.Application.Rulesets.Objects;
using Basil.Application.Rulesets.Services;
using Basil.Application.Shared.Actors;
using Basil.Application.Shared.Privileges;
using Basil.Domain.Users;

namespace Basil.Application.Rulesets.Capabilities;

public readonly struct ScopedRulesets
{
	private readonly RulesetLibrary _rulesets;
	private readonly UserPrivileges _privileges;

	internal ScopedRulesets(RulesetLibrary rulesets, IActor actor)
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

	public async Task<(RulesetUpdateResult Result, RulesetInstallResult? Install)> UpdateAsync(
		CancellationToken ct = default)
	{
		if (Lacks()) return (RulesetUpdateResult.Denied, null);

		var install = await _rulesets.Updater.InstallAsync(ct);
		if (install is null) return (RulesetUpdateResult.Busy, null);

		if (install is { Downloaded: true, RestartRequired: false }) _rulesets.Updated(install.Version);
		return (RulesetUpdateResult.Ok, install);
	}
}
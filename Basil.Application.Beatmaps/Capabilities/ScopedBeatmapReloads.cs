using Basil.Application.Beatmaps.Objects;
using Basil.Application.Rulesets.Objects;
using Basil.Application.Shared.Actors;
using Basil.Application.Shared.Privileges;
using Basil.Domain.Users;

namespace Basil.Application.Beatmaps.Capabilities;

public readonly struct ScopedBeatmapReloads
{
	private readonly BeatmapReloads _reloads;
	private readonly UserPrivileges _privileges;

	internal ScopedBeatmapReloads(BeatmapReloads reloads, IActor actor)
	{
		_reloads = reloads;
		_privileges = actor.EffectivePrivileges(reloads.Clock.GetUtcNow());
	}

	public RulesetUpdateResult Reload()
	{
		return ScopedPrivileges.Lacks(_privileges, UserPrivileges.Developer)
			? RulesetUpdateResult.Denied
			: _reloads.Reload();
	}
}
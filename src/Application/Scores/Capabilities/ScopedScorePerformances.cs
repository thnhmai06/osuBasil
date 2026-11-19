using Basil.Application.Scores.Objects;
using Basil.Application.Shared.Actors;
using Basil.Application.Shared.Privileges;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Capabilities;

public readonly struct ScopedScorePerformances
{
	private readonly ScorePerformances _performances;
	private readonly UserPrivileges _privileges;

	internal ScopedScorePerformances(ScorePerformances performances, IActor actor)
	{
		_performances = performances;
		_privileges = actor.EffectivePrivileges(performances.Clock.GetUtcNow());
	}

	public ScorePerformanceOutcome Recalculate(ScorePerformanceScope scope)
	{
		return ScopedPrivileges.Lacks(_privileges, UserPrivileges.Developer)
			? ScorePerformanceOutcome.Denied
			: _performances.Recalculate(scope);
	}
}
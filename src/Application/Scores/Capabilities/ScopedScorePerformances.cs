using Basil.Application.Scores.Objects;
using Basil.Application.Shared.Actors;
using Basil.Application.Shared.Privileges;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Capabilities;

/// <summary>The calculation of performance points as one actor may start it.</summary>
/// <remarks>Every operation requires <see cref="UserPrivileges.Developer" />.</remarks>
public readonly struct ScopedScorePerformances
{
	private readonly ScorePerformances _performances;
	private readonly UserPrivileges _privileges;

	internal ScopedScorePerformances(ScorePerformances performances, IActor actor)
	{
		_performances = performances;
		_privileges = actor.EffectivePrivileges(performances.Clock.GetUtcNow());
	}

	/// <summary>Starts calculating performance points in the background.</summary>
	/// <param name="scope">Which scores it covers.</param>
	/// <returns>Ok when it started; <see cref="ScorePerformanceOutcome.Busy" /> when a calculation is already running.</returns>
	public ScorePerformanceOutcome Recalculate(ScorePerformanceScope scope)
	{
		return ScopedPrivileges.Lacks(_privileges, UserPrivileges.Developer)
			? ScorePerformanceOutcome.Denied
			: _performances.Recalculate(scope);
	}
}

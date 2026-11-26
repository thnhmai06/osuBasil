namespace Basil.Tests.LoadTests;

/// <summary>One load level of a run, positioned on the run's timeline.</summary>
/// <param name="Index">The position of the step, from 1.</param>
/// <param name="Name">What the step is (for example <c>step 3</c> or <c>spike</c>).</param>
/// <param name="TargetSessions">The concurrent sessions <c>N</c> the step ramps to and holds.</param>
/// <param name="Start">When the step starts, counted from the end of the warm-up.</param>
/// <param name="End">When the step ends, counted from the end of the warm-up.</param>
public sealed record RunStep(int Index, string Name, int TargetSessions, TimeSpan Start, TimeSpan End);

/// <summary>The schedule of a run: warm-up, the steps one after the other, and the ramp-down.</summary>
/// <param name="WarmUp">The warm-up before the first step; it carries no judged load.</param>
/// <param name="Steps">The steps, in order.</param>
/// <param name="Tail">The ramp-down after the last step.</param>
public sealed record RunPlan(TimeSpan WarmUp, IReadOnlyList<RunStep> Steps, TimeSpan Tail)
{
	/// <summary>Gets the whole planned duration of the run.</summary>
	public TimeSpan Total => WarmUp + (Steps.Count == 0 ? TimeSpan.Zero : Steps[^1].End) + Tail;

	/// <summary>Lays the given levels one after the other.</summary>
	public static RunPlan Create(TimeSpan warmUp, TimeSpan tail, params (string Name, int Sessions, TimeSpan Duration)[] levels)
	{
		var steps = new List<RunStep>(levels.Length);
		var at = TimeSpan.Zero;
		foreach (var level in levels)
		{
			steps.Add(new RunStep(steps.Count + 1, level.Name, level.Sessions, at, at + level.Duration));
			at += level.Duration;
		}

		return new RunPlan(warmUp, steps, tail);
	}
}

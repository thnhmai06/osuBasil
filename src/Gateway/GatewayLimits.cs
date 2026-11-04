namespace Basil.Gateway;

/// <summary>The limits of the second admission tier; an instance never changes, a new one replaces it.</summary>
public sealed record GatewayLimits
{
	/// <summary>Gets how many requests a <see cref="AdmissionTier.Standard" /> subject may make in the window.</summary>
	public int Requests { get; init; } = 120;

	/// <summary>Gets the length of the window.</summary>
	public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(1);

	/// <summary>Gets the factor applied to <see cref="Requests" /> for an <see cref="AdmissionTier.Elevated" /> subject.</summary>
	public int ElevatedMultiplier { get; init; } = 4;

	/// <summary>Gets how many requests a subject of the tier may make in the window.</summary>
	/// <param name="tier">The tier of the subject.</param>
	/// <returns>The allowance.</returns>
	public int AllowanceOf(AdmissionTier tier)
	{
		return tier == AdmissionTier.Elevated ? Requests * ElevatedMultiplier : Requests;
	}
}

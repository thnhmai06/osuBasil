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

	/// <summary>Gets how many <see cref="WorkClass.Interactive" /> requests may run at once.</summary>
	public int InteractiveConcurrency { get; init; } = 256;

	/// <summary>Gets how many <see cref="WorkClass.Listing" /> requests may run at once.</summary>
	public int ListingConcurrency { get; init; } = 32;

	/// <summary>Gets how many <see cref="WorkClass.Upload" /> requests may run at once.</summary>
	public int UploadConcurrency { get; init; } = 8;

	/// <summary>Gets how many <see cref="WorkClass.Live" /> requests may run at once.</summary>
	public int LiveConcurrency { get; init; } = 1024;

	/// <summary>Gets the load from which the server reports <see cref="AdmissionHealth.Degraded" />.</summary>
	public double DegradedAbove { get; init; } = 0.5;

	/// <summary>Gets the load from which <see cref="WorkClass.Listing" /> work is refused.</summary>
	public double ShedAbove { get; init; } = 0.7;

	/// <summary>Gets the load from which every kind of work is refused.</summary>
	public double HardShedAbove { get; init; } = 0.9;

	/// <summary>Gets how long a caller refused for load or for a full work class is told to wait.</summary>
	public TimeSpan BusyRetryAfter { get; init; } = TimeSpan.FromSeconds(5);

	/// <summary>Gets how many requests a subject of the tier may make in the window.</summary>
	/// <param name="tier">The tier of the subject.</param>
	/// <returns>The allowance.</returns>
	public int AllowanceOf(AdmissionTier tier)
	{
		return tier == AdmissionTier.Elevated ? Requests * ElevatedMultiplier : Requests;
	}

	/// <summary>Gets how many requests of the class may run at once.</summary>
	/// <param name="workClass">The kind of work.</param>
	/// <returns>The cap.</returns>
	public int ConcurrencyOf(WorkClass workClass)
	{
		return workClass switch
		{
			WorkClass.Listing => ListingConcurrency,
			WorkClass.Upload => UploadConcurrency,
			WorkClass.Live => LiveConcurrency,
			_ => InteractiveConcurrency
		};
	}

	/// <summary>Gets whether work of the class is refused at the load.</summary>
	/// <param name="workClass">The kind of work.</param>
	/// <param name="load">The load, between 0 and 1.</param>
	/// <returns><see langword="true" /> when the work is shed.</returns>
	public bool Sheds(WorkClass workClass, double load)
	{
		return load >= (workClass == WorkClass.Listing ? ShedAbove : HardShedAbove);
	}

	/// <summary>Gets how the server is doing at the load.</summary>
	/// <param name="load">The load, between 0 and 1.</param>
	/// <returns>The health.</returns>
	public AdmissionHealth HealthAt(double load)
	{
		return load >= ShedAbove ? AdmissionHealth.Shedding
			: load >= DegradedAbove ? AdmissionHealth.Degraded
			: AdmissionHealth.Healthy;
	}
}

using Basil.Gateway.Admission;

namespace Basil.Gateway.Limits;

public sealed record GatewayLimits
{
	public int Requests { get; init; } = 120;

	public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(1);

	public int ElevatedMultiplier { get; init; } = 4;

	public int InteractiveConcurrency { get; init; } = 256;

	public int ListingConcurrency { get; init; } = 32;

	public int UploadConcurrency { get; init; } = 8;

	public int LiveConcurrency { get; init; } = 1024;

	public int AssetConcurrency { get; init; } = 64;

	public int AssetRequests { get; init; } = 1200;

	public double DegradedAbove { get; init; } = 0.5;

	public double ShedAbove { get; init; } = 0.7;

	public double HardShedAbove { get; init; } = 0.9;

	public TimeSpan BusyRetryAfter { get; init; } = TimeSpan.FromSeconds(5);

	public int AllowanceOf(AdmissionTier tier)
	{
		return tier == AdmissionTier.Elevated ? Requests * ElevatedMultiplier : Requests;
	}

	public int ConcurrencyOf(WorkClass workClass)
	{
		return workClass switch
		{
			WorkClass.Listing => ListingConcurrency,
			WorkClass.Upload => UploadConcurrency,
			WorkClass.Live => LiveConcurrency,
			WorkClass.Asset => AssetConcurrency,
			// A poll shares the cap of interactive work; the two are counted apart.
			_ => InteractiveConcurrency
		};
	}

	public bool Sheds(WorkClass workClass, double load)
	{
		return load >= (workClass is WorkClass.Listing or WorkClass.Asset ? ShedAbove : HardShedAbove);
	}

	public AdmissionHealth HealthAt(double load)
	{
		return load >= ShedAbove ? AdmissionHealth.Shedding
			: load >= DegradedAbove ? AdmissionHealth.Degraded
			: AdmissionHealth.Healthy;
	}
}
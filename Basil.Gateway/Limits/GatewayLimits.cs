using Basil.Gateway.Admission;

namespace Basil.Gateway.Limits;

public sealed record GatewayLimits
{
	/// <summary>Requests per window of interactive work: gameplay, chat, rooms and accounts.</summary>
	public int Requests { get; init; } = 120;

	public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(1);

	public int ElevatedMultiplier { get; init; } = 4;

	public int InteractiveConcurrency { get; init; } = 256;

	public int ListingConcurrency { get; init; } = 32;

	public int UploadConcurrency { get; init; } = 8;

	public int LiveConcurrency { get; init; } = 1024;

	public int AssetConcurrency { get; init; } = 64;

	/// <summary>Requests per window of listings and searches.</summary>
	public int ListingRequests { get; init; } = 60;

	/// <summary>Uploads per window.</summary>
	public int UploadRequests { get; init; } = 30;

	/// <summary>Opened live streams per window.</summary>
	public int LiveRequests { get; init; } = 30;

	/// <summary>Requests per window of the bancho poll of an open osu! client.</summary>
	public int PollRequests { get; init; } = 1200;

	/// <summary>Bytes a user may upload in an hour, counted from the declared length of each upload.</summary>
	public long UploadBytesPerHour { get; init; } = 1L << 30;

	public double DegradedAbove { get; init; } = 0.5;

	public double ShedAbove { get; init; } = 0.7;

	public double HardShedAbove { get; init; } = 0.9;

	public TimeSpan BusyRetryAfter { get; init; } = TimeSpan.FromSeconds(5);

	public int AllowanceOf(WorkClass workClass, AdmissionTier tier)
	{
		var allowance = workClass switch
		{
			WorkClass.Listing => ListingRequests,
			WorkClass.Upload => UploadRequests,
			WorkClass.Live => LiveRequests,
			WorkClass.Poll => PollRequests,
			_ => Requests
		};
		return tier == AdmissionTier.Elevated ? allowance * ElevatedMultiplier : allowance;
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

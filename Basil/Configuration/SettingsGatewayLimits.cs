using Basil.Application.Shared.Settings;
using Basil.Gateway.Limits;

namespace Basil.Configuration;

public sealed class SettingsGatewayLimits(ISettingsRepository settings)
{
	private (ServerSettings.AdmissionLimitSettings Source, GatewayLimits Limits)? _cached;

	public GatewayLimits Current()
	{
		var source = settings.Current.RateLimits.Admission;
		if (_cached is { } cached && ReferenceEquals(cached.Source, source)) return cached.Limits;
		var limits = new GatewayLimits
		{
			Requests = source.Requests,
			Window = source.Window,
			ElevatedMultiplier = source.ElevatedMultiplier,
			InteractiveConcurrency = source.InteractiveConcurrency,
			ListingConcurrency = source.ListingConcurrency,
			UploadConcurrency = source.UploadConcurrency,
			LiveConcurrency = source.LiveConcurrency,
			DegradedAbove = source.DegradedAbove,
			ShedAbove = source.ShedAbove,
			HardShedAbove = source.HardShedAbove,
			BusyRetryAfter = source.BusyRetryAfter,
			AssetConcurrency = source.AssetConcurrency,
			ListingRequests = source.ListingRequests,
			UploadRequests = source.UploadRequests,
			LiveRequests = source.LiveRequests,
			PollRequests = source.PollRequests,
			UploadBytesPerHour = source.UploadBytesPerHour
		};
		_cached = (source, limits);
		return limits;
	}
}
using Basil.Application.Common.Settings;
using Basil.Gateway;

namespace Basil.Configuration;

/// <summary>Supplies the admission limits from the server settings in memory, so a change applies to the next request.</summary>
/// <param name="settings">The server settings.</param>
public sealed class SettingsGatewayLimits(ISettingsRepository settings)
{
	private (ServerSettings.AdmissionLimitSettings Source, GatewayLimits Limits)? _cached;

	/// <summary>Gets the limits in force.</summary>
	/// <returns>The limits of the current settings.</returns>
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
			AssetRequests = source.AssetRequests
		};
		_cached = (source, limits);
		return limits;
	}
}
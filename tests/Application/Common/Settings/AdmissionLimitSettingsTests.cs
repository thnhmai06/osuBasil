using Basil.Application.Common.Settings;
using Basil.Application.Common.Settings;

namespace Basil.Application.Common.Tests.Settings;

/// <summary>
///     The admission limits are server settings with the gateway's defaults and an accepted boundary of 1 for the
///     hard shed load.
/// </summary>
public class AdmissionLimitSettingsTests
{
	[Fact]
	public void DefaultLimits_GatewayStandard()
	{
		var limits = new ServerSettings().RateLimits.Admission;

		Assert.Equal(120, limits.Requests);
		Assert.Equal(TimeSpan.FromMinutes(1), limits.Window);
		Assert.Equal(4, limits.ElevatedMultiplier);
		Assert.Equal((256, 32, 8, 1024),
			(limits.InteractiveConcurrency, limits.ListingConcurrency, limits.UploadConcurrency,
				limits.LiveConcurrency));
		Assert.Equal((0.5, 0.7, 0.9), (limits.DegradedAbove, limits.ShedAbove, limits.HardShedAbove));
		Assert.Equal(TimeSpan.FromSeconds(5), limits.BusyRetryAfter);
	}

	[Fact]
	public void HardShedOne_Accepted()
	{
		var limits = new ServerSettings.AdmissionLimitSettings(120, TimeSpan.FromMinutes(1), 4, 256, 32, 8, 1024, 0.5,
			0.7, 1,
			TimeSpan.FromSeconds(5));

		Assert.Equal(1, limits.HardShedAbove);
	}
}
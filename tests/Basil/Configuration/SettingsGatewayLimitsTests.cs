using Basil.Application.Content.Persistence;
using Basil.Configuration;
using Basil.Gateway;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Basil.Tests.Configuration;

/// <summary>The admission limits follow the server settings, and a change applies to the next decision.</summary>
public sealed class SettingsGatewayLimitsTests
{
	private static ServerSettings WithRequests(int requests)
	{
		return new ServerSettings
		{
			RateLimits = new ServerSettings.RateLimitSettings
			{
				Admission = new ServerSettings.AdmissionLimitSettings(requests, TimeSpan.FromMinutes(1), 4, 256, 32, 8,
					1024, 0.5,
					0.7, 0.9, TimeSpan.FromSeconds(5))
			}
		};
	}

	[Fact]
	public void The_limits_mirror_the_settings()
	{
		var settings = Substitute.For<ISettingsRepository>();
		settings.Current.Returns(WithRequests(7));

		var limits = new SettingsGatewayLimits(settings).Current();

		Assert.Equal(7, limits.Requests);
		Assert.Equal(TimeSpan.FromMinutes(1), limits.Window);
		Assert.Equal(0.9, limits.HardShedAbove);
	}

	[Fact]
	public void A_changed_setting_applies_to_the_next_admit_without_a_restart()
	{
		var settings = Substitute.For<ISettingsRepository>();
		settings.Current.Returns(WithRequests(1));
		var admission = new Admission(new FakeTimeProvider(), new SettingsGatewayLimits(settings).Current);
		var subject = new AdmissionSubject("a", AdmissionTier.Standard);

		Assert.True(admission.Admit(subject).Allowed);
		admission.Release(WorkClass.Interactive);
		Assert.False(admission.Admit(subject).Allowed);
		settings.Current.Returns(WithRequests(5));

		Assert.True(admission.Admit(subject).Allowed);
	}
}
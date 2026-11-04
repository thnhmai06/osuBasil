using Microsoft.Extensions.Time.Testing;

namespace Basil.Gateway.Tests;

/// <summary>
///     Tier 2 admits a subject up to its allowance per window: <c>Requests</c> for Standard, <c>Requests *
///     ElevatedMultiplier</c> for Elevated; the next request is denied with a retry delay.
/// </summary>
public sealed class AdmissionTests
{
	private static readonly GatewayLimits Small = new() { Requests = 3, Window = TimeSpan.FromMinutes(1), ElevatedMultiplier = 2 };

	private static AdmissionSubject Standard(string key) => new(key, AdmissionTier.Standard);

	[Fact]
	public void RequestsUnderTheLimit_AreAllowed()
	{
		var admission = new Admission(new FakeTimeProvider(), Small);

		for (var i = 0; i < 3; i++) Assert.True(admission.Admit(Standard("a")).Allowed);
	}

	[Fact]
	public void TheRequestOverTheLimit_IsDeniedWithARetryDelay()
	{
		var admission = new Admission(new FakeTimeProvider(), Small);
		for (var i = 0; i < 3; i++) admission.Admit(Standard("a"));

		var decision = admission.Admit(Standard("a"));

		Assert.False(decision.Allowed);
		Assert.Equal(AdmissionDenial.RateExceeded, decision.Reason);
		Assert.Equal(TimeSpan.FromMinutes(1), decision.RetryAfter);
	}

	[Fact]
	public void AnElevatedSubject_GetsTheMultipliedAllowance()
	{
		var admission = new Admission(new FakeTimeProvider(), Small);
		var elevated = new AdmissionSubject("a", AdmissionTier.Elevated);

		for (var i = 0; i < 6; i++) Assert.True(admission.Admit(elevated).Allowed);
		Assert.False(admission.Admit(elevated).Allowed);
	}

	[Fact]
	public void DistinctKeys_HaveIndependentAllowances()
	{
		var admission = new Admission(new FakeTimeProvider(), Small);
		for (var i = 0; i < 3; i++) admission.Admit(Standard("a"));

		Assert.True(admission.Admit(Standard("b")).Allowed);
	}

	[Fact]
	public void ADeniedSubject_IsAllowedAgainWhenTheWindowPasses()
	{
		var clock = new FakeTimeProvider();
		var admission = new Admission(clock, Small);
		for (var i = 0; i < 4; i++) admission.Admit(Standard("a"));

		clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));

		Assert.True(admission.Admit(Standard("a")).Allowed);
	}

	[Fact]
	public void SwappedLimits_TakeEffectForTheNextRequest()
	{
		var admission = new Admission(new FakeTimeProvider(), Small);
		for (var i = 0; i < 3; i++) admission.Admit(Standard("a"));
		Assert.False(admission.Admit(Standard("a")).Allowed);

		admission.Apply(new GatewayLimits { Requests = 10, Window = TimeSpan.FromMinutes(1) });

		Assert.True(admission.Admit(Standard("a")).Allowed);
	}

	[Fact]
	public void DefaultLimits_AllowAtLeastOneRequest()
	{
		Assert.True(new Admission(new FakeTimeProvider()).Admit(Standard("a")).Allowed);
	}
}

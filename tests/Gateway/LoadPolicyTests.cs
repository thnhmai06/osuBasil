using Microsoft.Extensions.Time.Testing;

namespace Basil.Gateway.Tests;

/// <summary>
///     Work of a kind runs up to its own cap without affecting the other kinds; under load the listings are refused
///     first and everything later; the health follows the load.
/// </summary>
public sealed class LoadPolicyTests
{
	private static readonly GatewayLimits Limits = new()
	{
		Requests = 1000, ListingConcurrency = 2, InteractiveConcurrency = 3, UploadConcurrency = 1, LiveConcurrency = 1,
		DegradedAbove = 0.5, ShedAbove = 0.7, HardShedAbove = 0.9
	};

	private static readonly AdmissionSubject Subject = new("a", AdmissionTier.Standard);

	private static Admission NewAdmission(LoadSignal? load = null)
	{
		return new Admission(new FakeTimeProvider(), Limits, load);
	}

	[Theory]
	[InlineData(WorkClass.Interactive, 3)]
	[InlineData(WorkClass.Listing, 2)]
	[InlineData(WorkClass.Upload, 1)]
	[InlineData(WorkClass.Live, 1)]
	public void AClass_IsDeniedAsSaturatedPastItsCap(WorkClass workClass, int cap)
	{
		var admission = NewAdmission();
		for (var i = 0; i < cap; i++) Assert.True(admission.Admit(Subject, workClass).Allowed);

		var decision = admission.Admit(Subject, workClass);

		Assert.False(decision.Allowed);
		Assert.Equal(AdmissionDenial.ClassSaturated, decision.Reason);
		Assert.Equal(Limits.BusyRetryAfter, decision.RetryAfter);
	}

	[Fact]
	public void ASaturatedClass_DoesNotAffectTheOthers()
	{
		var admission = NewAdmission();
		for (var i = 0; i < 3; i++) admission.Admit(Subject, WorkClass.Listing);

		Assert.True(admission.Admit(Subject, WorkClass.Interactive).Allowed);
	}

	[Fact]
	public void ReleasingAPlace_LetsTheClassAdmitAgain()
	{
		var admission = NewAdmission();
		admission.Admit(Subject, WorkClass.Upload);
		Assert.False(admission.Admit(Subject, WorkClass.Upload).Allowed);

		admission.Release(WorkClass.Upload);

		Assert.True(admission.Admit(Subject, WorkClass.Upload).Allowed);
	}

	[Fact]
	public void ADeniedRequest_DoesNotKeepAPlace()
	{
		var admission = NewAdmission();
		admission.Admit(Subject, WorkClass.Upload);
		for (var i = 0; i < 5; i++) admission.Admit(Subject, WorkClass.Upload);

		admission.Release(WorkClass.Upload);

		Assert.True(admission.Admit(Subject, WorkClass.Upload).Allowed);
	}

	[Fact]
	public void ARequestOverTheRateLimit_DoesNotKeepAPlace()
	{
		var admission = new Admission(new FakeTimeProvider(), Limits with { Requests = 1 });
		admission.Admit(Subject, WorkClass.Upload);
		admission.Release(WorkClass.Upload);
		Assert.Equal(AdmissionDenial.RateExceeded, admission.Admit(Subject, WorkClass.Upload).Reason);

		admission.Apply(Limits);

		Assert.True(admission.Admit(Subject, WorkClass.Upload).Allowed);
	}

	// Decision table of the shed order: [class, load, shed].
	[Theory]
	[InlineData(WorkClass.Listing, 0.0, false)]
	[InlineData(WorkClass.Listing, 0.69, false)]
	[InlineData(WorkClass.Listing, 0.7, true)]
	[InlineData(WorkClass.Listing, 1.0, true)]
	[InlineData(WorkClass.Interactive, 0.7, false)]
	[InlineData(WorkClass.Interactive, 0.89, false)]
	[InlineData(WorkClass.Interactive, 0.9, true)]
	[InlineData(WorkClass.Live, 0.89, false)]
	[InlineData(WorkClass.Live, 0.9, true)]
	[InlineData(WorkClass.Upload, 0.89, false)]
	[InlineData(WorkClass.Upload, 0.9, true)]
	public void TheShedOrder_RefusesListingsFirstAndEverythingLater(WorkClass workClass, double load, bool shed)
	{
		Assert.Equal(shed, Limits.Sheds(workClass, load));
	}

	[Fact]
	public void AShedClass_IsDeniedAsShedWithoutTakingAPlace()
	{
		var load = new LoadSignal();
		var admission = NewAdmission(load);
		load.Report(8, 10);

		var decision = admission.Admit(Subject, WorkClass.Listing);

		Assert.Equal(AdmissionDenial.Shed, decision.Reason);
		Assert.Equal(Limits.BusyRetryAfter, decision.RetryAfter);
		load.Report(0, 10);
		Assert.True(admission.Admit(Subject, WorkClass.Listing).Allowed);
		Assert.True(admission.Admit(Subject, WorkClass.Listing).Allowed);
	}

	[Fact]
	public void UnderShedding_InteractiveWorkIsStillAdmitted()
	{
		var load = new LoadSignal();
		var admission = NewAdmission(load);
		load.Report(8, 10);

		Assert.True(admission.Admit(Subject, WorkClass.Interactive).Allowed);
	}

	[Theory]
	[InlineData(0.0, AdmissionHealth.Healthy)]
	[InlineData(0.49, AdmissionHealth.Healthy)]
	[InlineData(0.5, AdmissionHealth.Degraded)]
	[InlineData(0.69, AdmissionHealth.Degraded)]
	[InlineData(0.7, AdmissionHealth.Shedding)]
	[InlineData(1.0, AdmissionHealth.Shedding)]
	public void Health_FollowsTheLoad(double load, AdmissionHealth expected)
	{
		var signal = new LoadSignal();
		signal.Report(load * 100, 100);

		Assert.Equal(expected, NewAdmission(signal).Health);
	}

	[Fact]
	public void ANewAdmission_IsHealthy()
	{
		Assert.Equal(AdmissionHealth.Healthy, new Admission(new FakeTimeProvider()).Health);
	}

	[Theory]
	[InlineData(5, 10, 0.5)]
	[InlineData(20, 10, 1.0)]
	[InlineData(-3, 10, 0.0)]
	[InlineData(5, 0, 0.0)]
	public void ALoadSignal_IsTheClampedFractionOfItsCapacity(double used, double capacity, double expected)
	{
		var signal = new LoadSignal();

		signal.Report(used, capacity);

		Assert.Equal(expected, signal.Value);
	}

	[Fact]
	public void AnEntryPoint_DefaultsToInteractiveWork()
	{
		Assert.Equal(WorkClass.Interactive, new AdmissionPolicyAttribute().Class);
	}
}

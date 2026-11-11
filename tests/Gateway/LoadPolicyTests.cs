using Basil.Gateway.Admission;
using Basil.Gateway.Limits;
using Microsoft.Extensions.Time.Testing;
using AdmissionGate = Basil.Gateway.Admission.Admission;

namespace Basil.Gateway.Tests;

/// <summary>
///     Work of a kind runs up to its own cap without affecting the other kinds; under load the listings are refused
///     first and everything later; the health follows the load.
/// </summary>
public sealed class LoadPolicyTests
{
	private static readonly GatewayLimits Limits = new()
	{
		Requests = 1000, ListingConcurrency = 2, InteractiveConcurrency = 3, UploadConcurrency = 1, LiveConcurrency = 1, AssetConcurrency = 2, AssetRequests = 3,
		DegradedAbove = 0.5, ShedAbove = 0.7, HardShedAbove = 0.9
	};

	private static readonly AdmissionSubject Subject = new("a", AdmissionTier.Standard);

	private static AdmissionGate NewAdmission(LoadSignal? load = null)
	{
		return new AdmissionGate(new FakeTimeProvider(), Limits, load);
	}

	[Theory]
	[InlineData(WorkClass.Interactive, 3)]
	[InlineData(WorkClass.Listing, 2)]
	[InlineData(WorkClass.Upload, 1)]
	[InlineData(WorkClass.Live, 1)]
	[InlineData(WorkClass.Asset, 2)]
	public void ClassSaturated_DeniedPastCap(WorkClass workClass, int cap)
	{
		var admission = NewAdmission();
		for (var i = 0; i < cap; i++) Assert.True(admission.Admit(Subject, workClass).Allowed);

		var decision = admission.Admit(Subject, workClass);

		Assert.False(decision.Allowed);
		Assert.Equal(AdmissionDenial.ClassSaturated, decision.Reason);
		Assert.Equal(Limits.BusyRetryAfter, decision.RetryAfter);
	}

	[Fact]
	public void SaturatedClass_OthersUnaffected()
	{
		var admission = NewAdmission();
		for (var i = 0; i < 3; i++) admission.Admit(Subject, WorkClass.Listing);

		Assert.True(admission.Admit(Subject).Allowed);
	}

	[Fact]
	public void ReleasedPlace_ClassAdmitsAgain()
	{
		var admission = NewAdmission();
		admission.Admit(Subject, WorkClass.Upload);
		Assert.False(admission.Admit(Subject, WorkClass.Upload).Allowed);

		admission.Release(WorkClass.Upload);

		Assert.True(admission.Admit(Subject, WorkClass.Upload).Allowed);
	}

	[Fact]
	public void DeniedRequest_NoPlaceKept()
	{
		var admission = NewAdmission();
		admission.Admit(Subject, WorkClass.Upload);
		for (var i = 0; i < 5; i++) admission.Admit(Subject, WorkClass.Upload);

		admission.Release(WorkClass.Upload);

		Assert.True(admission.Admit(Subject, WorkClass.Upload).Allowed);
	}

	[Fact]
	public void OverRateLimit_NoPlaceKept()
	{
		var admission = new AdmissionGate(new FakeTimeProvider(), Limits with { Requests = 1 });
		admission.Admit(Subject, WorkClass.Upload);
		admission.Release(WorkClass.Upload);
		Assert.Equal(AdmissionDenial.RateExceeded, admission.Admit(Subject, WorkClass.Upload).Reason);

		admission.Apply(Limits);

		Assert.True(admission.Admit(Subject, WorkClass.Upload).Allowed);
	}

	[Fact]
	public void AssetsOwnAllowance_SeparateFromSubject()
	{
		var admission = new AdmissionGate(new FakeTimeProvider(), Limits with { Requests = 1, AssetConcurrency = 10 });

		var assets = Enumerable.Range(0, 4).Select(_ => admission.Admit(Subject, WorkClass.Asset)).ToArray();

		Assert.Equal([true, true, true, false], assets.Select(decision => decision.Allowed));
		Assert.Equal(AdmissionDenial.RateExceeded, assets[3].Reason);
		Assert.True(admission.Admit(Subject).Allowed);
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
	[InlineData(WorkClass.Asset, 0.69, false)]
	[InlineData(WorkClass.Asset, 0.7, true)]
	[InlineData(WorkClass.Upload, 0.89, false)]
	[InlineData(WorkClass.Upload, 0.9, true)]
	public void ShedOrder_ListingsRefusedFirst(WorkClass workClass, double load, bool shed)
	{
		Assert.Equal(shed, Limits.Sheds(workClass, load));
	}

	[Fact]
	public void ShedClass_DeniedNoPlace()
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
	public void UnderShedding_InteractiveAdmitted()
	{
		var load = new LoadSignal();
		var admission = NewAdmission(load);
		load.Report(8, 10);

		Assert.True(admission.Admit(Subject).Allowed);
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

	[Theory]
	[InlineData(WorkClass.Interactive, 1, AdmissionHealth.Healthy)]
	[InlineData(WorkClass.Interactive, 2, AdmissionHealth.Degraded)]
	[InlineData(WorkClass.Interactive, 3, AdmissionHealth.Shedding)]
	[InlineData(WorkClass.Upload, 1, AdmissionHealth.Shedding)]
	[InlineData(WorkClass.Live, 1, AdmissionHealth.Shedding)]
	[InlineData(WorkClass.Listing, 2, AdmissionHealth.Healthy)]
	[InlineData(WorkClass.Asset, 2, AdmissionHealth.Healthy)]
	public void Health_FollowsUnshedFullness(WorkClass workClass, int running,
		AdmissionHealth expected)
	{
		var admission = NewAdmission();
		for (var i = 0; i < running; i++) Assert.True(admission.Admit(Subject, workClass).Allowed);

		Assert.Equal(expected, admission.Health);
	}

	[Fact]
	public void InteractiveFull_ShedThenBack()
	{
		var admission = NewAdmission();
		for (var i = 0; i < 3; i++) admission.Admit(Subject);

		var shed = admission.Admit(Subject, WorkClass.Listing);
		for (var i = 0; i < 3; i++) admission.Release(WorkClass.Interactive);

		Assert.Equal((false, AdmissionDenial.Shed, true),
			(shed.Allowed, shed.Reason, admission.Admit(Subject, WorkClass.Listing).Allowed));
	}

	[Fact]
	public void ReportedLoad_CountsWhenEmpty()
	{
		var signal = new LoadSignal();
		signal.Report(0.8, 1);

		Assert.Equal(AdmissionHealth.Shedding, NewAdmission(signal).Health);
	}

	[Fact]
	public void ANewAdmission_IsHealthy()
	{
		Assert.Equal(AdmissionHealth.Healthy, new AdmissionGate(new FakeTimeProvider()).Health);
	}

	[Theory]
	[InlineData(5, 10, 0.5)]
	[InlineData(20, 10, 1.0)]
	[InlineData(-3, 10, 0.0)]
	[InlineData(5, 0, 0.0)]
	public void LoadSignal_ClampedFraction(double used, double capacity, double expected)
	{
		var signal = new LoadSignal();

		signal.Report(used, capacity);

		Assert.Equal(expected, signal.Value);
	}

	[Fact]
	public void EntryPoint_DefaultsInteractive()
	{
		Assert.Equal(WorkClass.Interactive, new AdmissionPolicyAttribute().Class);
	}
}
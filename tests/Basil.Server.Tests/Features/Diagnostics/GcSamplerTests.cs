using System.Runtime;
using Basil.Server.Features.Diagnostics;

namespace Basil.Server.Tests.Features.Diagnostics;

/// <summary>Verifies <see cref="GcSampler" />'s cumulative counters and its derived time-in-GC figure.</summary>
public class GcSamplerTests
{
	[Fact]
	public void CollectionCountsAreNeverNegative()
	{
		var sampler = new GcSampler();

		var sample = sampler.Sample();

		Assert.True(sample.Gen0Collections >= 0);
		Assert.True(sample.Gen1Collections >= 0);
		Assert.True(sample.Gen2Collections >= 0);
	}

	/// <summary>The first sample has no prior reading to diff against, so it reports no time in GC.</summary>
	[Fact]
	public void TheFirstSampleReportsZeroTimeInGc()
	{
		var sampler = new GcSampler();

		var sample = sampler.Sample();

		Assert.Equal(0, sample.TimeInGcPercent);
	}

	[Fact]
	public void ReportsTheConfiguredGarbageCollectorMode()
	{
		var sampler = new GcSampler();

		var sample = sampler.Sample();

		Assert.Equal(GCSettings.IsServerGC, sample.IsServerGc);
		Assert.Equal(GCSettings.LatencyMode, sample.LatencyMode);
	}

	[Fact]
	public void TotalAllocatedBytesIsNeverNegative()
	{
		var sampler = new GcSampler();

		var sample = sampler.Sample();

		Assert.True(sample.TotalAllocatedBytes >= 0);
	}
}

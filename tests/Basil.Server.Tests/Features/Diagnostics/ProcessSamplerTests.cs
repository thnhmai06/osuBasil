using Basil.Server.Features.Diagnostics;

namespace Basil.Server.Tests.Features.Diagnostics;

/// <summary>
///     Verifies <see cref="ProcessSampler" /> shares one process refresh across every field it
///     reports, rather than paying the refresh cost per metric.
/// </summary>
public class ProcessSamplerTests
{
	/// <summary>
	///     One process refresh serves every process and memory metric in a tick. A fresh process
	///     handle or an explicit refresh is a syscall costing several milliseconds, so refreshing per
	///     metric would make the diagnostic loop a measurable load on the server it exists to observe.
	/// </summary>
	[Fact]
	public void ASampleRefreshesTheProcessExactlyOnce()
	{
		var sampler = new ProcessSampler();

		_ = sampler.Sample();

		Assert.Equal(1, sampler.RefreshCountForTest);
	}

	[Fact]
	public void EachCallRefreshesTheProcessExactlyOnceMore()
	{
		var sampler = new ProcessSampler();

		_ = sampler.Sample();
		_ = sampler.Sample();
		_ = sampler.Sample();

		Assert.Equal(3, sampler.RefreshCountForTest);
	}

	/// <summary>The first sample has no prior reading to diff against, so it reports no CPU usage.</summary>
	[Fact]
	public void TheFirstSampleReportsZeroCpuUsage()
	{
		var sampler = new ProcessSampler();

		var sample = sampler.Sample();

		Assert.Equal(0, sample.CpuUsagePercent);
	}

	[Fact]
	public void ASampleReportsThisProcessId()
	{
		var sampler = new ProcessSampler();

		var sample = sampler.Sample();

		Assert.Equal(Environment.ProcessId, sample.ProcessId);
	}
}

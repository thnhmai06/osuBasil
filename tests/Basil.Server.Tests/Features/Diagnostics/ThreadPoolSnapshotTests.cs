using System.Reflection;
using Basil.Server.Features.Diagnostics;

namespace Basil.Server.Tests.Features.Diagnostics;

/// <summary>Verifies <see cref="ThreadPoolSnapshot" /> reports plausible values and stays off the vestigial I/O completion port counters.</summary>
public class ThreadPoolSnapshotTests
{
	[Fact]
	public void CaptureReportsAPositiveMaxWorkerThreadCount()
	{
		var sample = ThreadPoolSnapshot.Capture();

		Assert.True(sample.MaxWorkerThreads > 0);
		Assert.True(sample.MinWorkerThreads > 0);
	}

	[Fact]
	public void CaptureReportsNonNegativeCounters()
	{
		var sample = ThreadPoolSnapshot.Capture();

		Assert.True(sample.ThreadCount >= 0);
		Assert.True(sample.AvailableWorkerThreads >= 0);
		Assert.True(sample.PendingWorkItemCount >= 0);
		Assert.True(sample.CompletedWorkItemCount >= 0);
		Assert.True(sample.LockContentionCount >= 0);
	}

	/// <summary>
	///     The legacy I/O completion port thread counts are fixed values on the portable thread pool
	///     and carry no operational signal, so they are deliberately never exposed here.
	/// </summary>
	[Fact]
	public void TheSampleNeverExposesCompletionPortThreadCounts()
	{
		var properties = typeof(ThreadPoolSample).GetProperties(BindingFlags.Public | BindingFlags.Instance);

		Assert.DoesNotContain(properties, p =>
			p.Name.Contains("Iocp", StringComparison.OrdinalIgnoreCase) ||
			p.Name.Contains("CompletionPort", StringComparison.OrdinalIgnoreCase));
	}
}

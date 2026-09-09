using Basil.Server.Features.Diagnostics;

namespace Basil.Server.Tests.Features.Diagnostics;

/// <summary>Verifies <see cref="RuntimeSnapshot" /> reports plausible values and is read once, not per call.</summary>
public class RuntimeSnapshotTests
{
	[Fact]
	public void CaptureReportsPlausibleHardwareAndRuntimeValues()
	{
		var sample = RuntimeSnapshot.Capture();

		Assert.True(sample.ProcessorCount > 0);
		Assert.False(string.IsNullOrWhiteSpace(sample.FrameworkDescription));
		Assert.False(string.IsNullOrWhiteSpace(sample.OSDescription));
	}

	/// <summary>
	///     Every field is fixed for the process's lifetime, so repeated calls must return the exact
	///     same cached record rather than re-reading the underlying APIs -- most importantly
	///     <see cref="GC.GetConfigurationVariables" />, which allocates a dictionary on every call.
	/// </summary>
	[Fact]
	public void CaptureReturnsTheSameCachedInstanceEveryTime()
	{
		var first = RuntimeSnapshot.Capture();
		var second = RuntimeSnapshot.Capture();

		Assert.Same(first, second);
	}
}

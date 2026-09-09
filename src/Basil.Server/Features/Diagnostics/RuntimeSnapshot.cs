using System.Runtime;
using System.Runtime.InteropServices;

namespace Basil.Server.Features.Diagnostics;

/// <summary>The runtime and hardware configuration the process is running under.</summary>
/// <param name="FrameworkDescription">The human-readable .NET runtime name and version, e.g. <c>.NET 10.0.11</c>.</param>
/// <param name="ProcessorCount">The number of logical processors available to the process.</param>
/// <param name="ProcessArchitecture">This process's own instruction-set architecture.</param>
/// <param name="OSArchitecture">The host operating system's instruction-set architecture.</param>
/// <param name="OSDescription">The host operating system's name and version.</param>
/// <param name="IsServerGc">Whether the process is running the server garbage collector.</param>
/// <param name="IsConcurrentGcEnabled">Whether background (concurrent) collection is configured.</param>
/// <remarks>
///     <see cref="ProcessArchitecture" /> and <see cref="OSArchitecture" /> are kept as separate
///     fields rather than collapsed into one: a process can run under emulation (e.g. an x86
///     process on an x64 host), so the two can legitimately disagree.
/// </remarks>
public sealed record RuntimeSample(
	string FrameworkDescription,
	int ProcessorCount,
	Architecture ProcessArchitecture,
	Architecture OSArchitecture,
	string OSDescription,
	bool IsServerGc,
	bool IsConcurrentGcEnabled);

/// <summary>Reads the process's runtime and hardware configuration once and caches it for the process's lifetime.</summary>
/// <remarks>
///     Every field this type reports is fixed for as long as the process runs: the framework
///     version, logical processor count, process/OS architecture and garbage collector mode are all
///     decided before or at process start and cannot change without a restart. Reading them on
///     every broadcast tick would therefore only repeat the same answer at a cost -- most notably
///     <see cref="GC.GetConfigurationVariables" />, which allocates a fresh dictionary on every
///     call -- so this type reads each field exactly once, on first use, and hands back the same
///     cached record forever after. The concurrent-GC flag specifically reuses
///     <see cref="GcSampler.ConcurrentGcEnabled" /> instead of reading the configuration dictionary
///     a second time for the same fixed value.
/// </remarks>
public static class RuntimeSnapshot
{
	private static readonly RuntimeSample Cached = new(
		RuntimeInformation.FrameworkDescription,
		Environment.ProcessorCount,
		RuntimeInformation.ProcessArchitecture,
		RuntimeInformation.OSArchitecture,
		RuntimeInformation.OSDescription,
		GCSettings.IsServerGC,
		GcSampler.ConcurrentGcEnabled);

	/// <summary>Returns the runtime and hardware configuration captured once at process start.</summary>
	public static RuntimeSample Capture() => Cached;
}

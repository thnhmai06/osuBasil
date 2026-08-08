namespace Basil.LoadTests.Infrastructure.Metrics;

/// <summary>
///     One timestamped resource reading. Every field is nullable — a host that cannot observe a given
///     field (see <see cref="Hosting.ServerHostCapabilities" />) reports it as absent, never as zero, so
///     an unavailable metric can never be misread as "measured and equal to zero".
/// </summary>
/// <param name="TimestampUtc">When the sample was taken.</param>
public sealed record ResourceSample(DateTimeOffset TimestampUtc)
{
	/// <summary>Process CPU usage, in percent of one core normalized to 100 == all cores busy.</summary>
	public double? CpuPercent { get; init; }

	/// <summary>Working set, in bytes.</summary>
	public long? WorkingSetBytes { get; init; }

	/// <summary>Private (committed) memory, in bytes.</summary>
	public long? PrivateMemoryBytes { get; init; }

	/// <summary>GC heap size, in bytes.</summary>
	public long? GcHeapBytes { get; init; }

	/// <summary>Cumulative gen0 collection count.</summary>
	public long? Gen0Collections { get; init; }

	/// <summary>Cumulative gen1 collection count.</summary>
	public long? Gen1Collections { get; init; }

	/// <summary>Cumulative gen2 collection count.</summary>
	public long? Gen2Collections { get; init; }

	/// <summary>Allocation rate, in bytes per second.</summary>
	public double? AllocRateBytesPerSecond { get; init; }

	/// <summary>Total managed + native thread count.</summary>
	public int? ThreadCount { get; init; }

	/// <summary>Open OS handle count (Windows) or file descriptor count (Linux).</summary>
	public int? HandleCount { get; init; }

	/// <summary>Threadpool thread count.</summary>
	public int? ThreadPoolThreads { get; init; }

	/// <summary>TCP connections currently open to the server's port.</summary>
	public int? TcpConnections { get; init; }

	/// <summary>Merges this sample with another, preferring non-null values from <paramref name="other" />.</summary>
	/// <param name="other">The sample to merge on top of this one.</param>
	/// <returns>A new sample combining both, keeping this sample's timestamp.</returns>
	public ResourceSample MergeWith(ResourceSample other)
	{
		return this with
		{
			CpuPercent = other.CpuPercent ?? CpuPercent,
			WorkingSetBytes = other.WorkingSetBytes ?? WorkingSetBytes,
			PrivateMemoryBytes = other.PrivateMemoryBytes ?? PrivateMemoryBytes,
			GcHeapBytes = other.GcHeapBytes ?? GcHeapBytes,
			Gen0Collections = other.Gen0Collections ?? Gen0Collections,
			Gen1Collections = other.Gen1Collections ?? Gen1Collections,
			Gen2Collections = other.Gen2Collections ?? Gen2Collections,
			AllocRateBytesPerSecond = other.AllocRateBytesPerSecond ?? AllocRateBytesPerSecond,
			ThreadCount = other.ThreadCount ?? ThreadCount,
			HandleCount = other.HandleCount ?? HandleCount,
			ThreadPoolThreads = other.ThreadPoolThreads ?? ThreadPoolThreads,
			TcpConnections = other.TcpConnections ?? TcpConnections
		};
	}
}

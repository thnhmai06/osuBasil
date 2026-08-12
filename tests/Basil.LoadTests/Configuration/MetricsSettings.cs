namespace Basil.LoadTests.Configuration;

/// <summary>Settings for the in-process .NET runtime counters collector.</summary>
public sealed class DotnetCountersSettings
{
	/// <summary>Whether to collect counters at all. A failed attach degrades this to disabled, not a failure.</summary>
	public bool Enabled { get; init; } = true;

	/// <summary>How often the runtime publishes counter events, in seconds.</summary>
	public int RefreshIntervalSeconds { get; init; } = 1;
}

/// <summary>Resource-sampling settings shared by every scenario in a run.</summary>
public sealed class MetricsSettings
{
	/// <summary>How often <see cref="Hosting.IServerHost.CollectMetricsAsync" /> is polled during a run.</summary>
	public int SampleIntervalSeconds { get; init; } = 5;

	/// <summary>Settings for the GC/allocation/threadpool counters collector.</summary>
	public DotnetCountersSettings DotnetCounters { get; init; } = new();

	/// <summary>Gets <see cref="SampleIntervalSeconds" /> as a <see cref="TimeSpan" />.</summary>
	public TimeSpan SampleInterval => TimeSpan.FromSeconds(SampleIntervalSeconds);
}
using System.Globalization;

namespace Basil.LoadTests.Infrastructure.Metrics;

/// <summary>Min/mean/max over a series of samples for one field. All null when the field was never observed.</summary>
public sealed record FieldAggregate(double? Min, double? Mean, double? Max);

/// <summary>
///     Accumulates <see cref="ResourceSample" />s over the course of a run and turns them into the
///     <c>resources.csv</c> artifact and the aggregate figures <c>run.json</c>/<c>summary.md</c> report.
/// </summary>
public sealed class ResourceTimeline
{
	private readonly List<ResourceSample> _samples = [];

	/// <summary>Gets every sample recorded so far, in the order they were added.</summary>
	public IReadOnlyList<ResourceSample> Samples => _samples;

	/// <summary>Records one sample.</summary>
	public void Add(ResourceSample sample)
	{
		_samples.Add(sample);
	}

	/// <summary>Computes min/mean/max for every field, keyed by field name. Unset fields are all-null.</summary>
	public IReadOnlyDictionary<string, FieldAggregate> Aggregate()
	{
		var result = new Dictionary<string, FieldAggregate>();
		Add(result, "CpuPercent", _samples.Select(s => s.CpuPercent));
		Add(result, "WorkingSetBytes", _samples.Select(s => (double?)s.WorkingSetBytes));
		Add(result, "PrivateMemoryBytes", _samples.Select(s => (double?)s.PrivateMemoryBytes));
		Add(result, "GcHeapBytes", _samples.Select(s => (double?)s.GcHeapBytes));
		Add(result, "Gen0Collections", _samples.Select(s => (double?)s.Gen0Collections));
		Add(result, "Gen1Collections", _samples.Select(s => (double?)s.Gen1Collections));
		Add(result, "Gen2Collections", _samples.Select(s => (double?)s.Gen2Collections));
		Add(result, "AllocRateBytesPerSecond", _samples.Select(s => s.AllocRateBytesPerSecond));
		Add(result, "ThreadCount", _samples.Select(s => (double?)s.ThreadCount));
		Add(result, "HandleCount", _samples.Select(s => (double?)s.HandleCount));
		Add(result, "ThreadPoolThreads", _samples.Select(s => (double?)s.ThreadPoolThreads));
		Add(result, "TcpConnections", _samples.Select(s => (double?)s.TcpConnections));
		Add(result, "ThreadPoolQueueLength", _samples.Select(s => (double?)s.ThreadPoolQueueLength));
		Add(result, "GcPausePercent", _samples.Select(s => s.GcPausePercent));
		Add(result, "TotalMachineCpuPercent", _samples.Select(s => s.TotalMachineCpuPercent));
		return result;
	}

	/// <summary>Writes every recorded sample to a CSV file, unavailable fields left as empty cells.</summary>
	/// <param name="path">The destination file path; its directory is created if missing.</param>
	public void WriteCsv(string path)
	{
		var directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

		using var writer = new StreamWriter(path);
		writer.WriteLine("TimestampUtc,CpuPercent,WorkingSetBytes,PrivateMemoryBytes,GcHeapBytes," +
		                 "Gen0Collections,Gen1Collections,Gen2Collections,AllocRateBytesPerSecond," +
		                 "ThreadCount,HandleCount,ThreadPoolThreads,TcpConnections,ThreadPoolQueueLength," +
		                 "GcPausePercent,TotalMachineCpuPercent");

		foreach (var s in _samples)
			writer.WriteLine(string.Join(',',
				s.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
				Cell(s.CpuPercent), Cell(s.WorkingSetBytes), Cell(s.PrivateMemoryBytes), Cell(s.GcHeapBytes),
				Cell(s.Gen0Collections), Cell(s.Gen1Collections), Cell(s.Gen2Collections),
				Cell(s.AllocRateBytesPerSecond), Cell(s.ThreadCount), Cell(s.HandleCount),
				Cell(s.ThreadPoolThreads), Cell(s.TcpConnections), Cell(s.ThreadPoolQueueLength),
				Cell(s.GcPausePercent), Cell(s.TotalMachineCpuPercent)));
	}

	private static void Add(Dictionary<string, FieldAggregate> result, string name, IEnumerable<double?> values)
	{
		var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
		result[name] = present.Length == 0
			? new FieldAggregate(null, null, null)
			: new FieldAggregate(present.Min(), present.Average(), present.Max());
	}

	private static string Cell<T>(T? value) where T : struct
	{
		return value switch
		{
			null => "",
			double d => d.ToString("F3", CultureInfo.InvariantCulture),
			_ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
		};
	}
}
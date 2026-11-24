using System.Diagnostics;
using System.Globalization;

namespace LoadHarness;

/// <summary>Samples a container's CPU and memory with <c>docker stats --no-stream</c> while the run is measured.</summary>
internal sealed class ContainerSampler(string container)
{
	private readonly List<(double Cpu, double MemoryMiB)> _samples = [];

	public async Task RunAsync(Recorder recorder, CancellationToken ct)
	{
		try
		{
			while (!ct.IsCancellationRequested)
			{
				if (recorder.Measuring && await SampleAsync(ct) is { } sample) _samples.Add(sample);
				await Task.Delay(TimeSpan.FromSeconds(5), ct);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	public ContainerReport? Report()
	{
		return _samples.Count == 0
			? null
			: new ContainerReport(container, _samples.Count, Math.Round(_samples.Average(s => s.Cpu), 1),
				Math.Round(_samples.Max(s => s.Cpu), 1), Math.Round(_samples.Average(s => s.MemoryMiB), 1),
				Math.Round(_samples.Max(s => s.MemoryMiB), 1));
	}

	private async Task<(double, double)?> SampleAsync(CancellationToken ct)
	{
		try
		{
			using var process = Process.Start(new ProcessStartInfo("docker",
				["stats", "--no-stream", "--format", "{{.CPUPerc}}|{{.MemUsage}}", container])
			{
				RedirectStandardOutput = true
			})!;
			var output = await process.StandardOutput.ReadToEndAsync(ct);
			await process.WaitForExitAsync(ct);
			// "12.34%|256.5MiB / 7.6GiB"
			var parts = output.Trim().Split('|');
			if (parts.Length != 2) return null;
			var cpu = double.Parse(parts[0].TrimEnd('%'), CultureInfo.InvariantCulture);
			var used = parts[1].Split('/')[0].Trim();
			var unit = used.TrimStart("0123456789.".ToCharArray());
			var number = double.Parse(used[..^unit.Length], CultureInfo.InvariantCulture);
			var mib = unit switch
			{
				"B" => number / 1048576,
				"KiB" or "kB" => number / 1024,
				"MiB" or "MB" => number,
				"GiB" or "GB" => number * 1024,
				_ => double.NaN
			};
			return double.IsNaN(mib) ? null : (cpu, mib);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			return null;
		}
	}
}

internal sealed record ContainerReport(
	string Container,
	int Samples,
	double CpuPercentAverage,
	double CpuPercentMax,
	double MemoryMiBAverage,
	double MemoryMiBMax);

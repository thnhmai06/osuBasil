using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Basil.LoadTests.Infrastructure.Metrics;

/// <summary>
///     Samples CPU% and memory usage for a Docker Compose service via <c>docker stats --no-stream</c>.
///     Docker's own stats surface has no GC/thread/handle view into the container, so those fields are
///     always left <see langword="null" /> here — <see cref="Hosting.DockerServerHost" />'s
///     <see cref="Hosting.ServerHostCapabilities" /> reflects that rather than guessing.
/// </summary>
/// <param name="containerName">The container name or id to query.</param>
public sealed class DockerStatsSampler(string containerName) : IResourceSampler
{
	public Task StartAsync(CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	public async Task<ResourceSample> SampleAsync(CancellationToken cancellationToken = default)
	{
		var now = DateTimeOffset.UtcNow;

		using var process = Process.Start(new ProcessStartInfo("docker",
			$"stats {containerName} --no-stream --format \"{{{{json .}}}}\"")
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		});
		if (process is null) return new ResourceSample(now);

		var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
		await process.WaitForExitAsync(cancellationToken);
		if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output)) return new ResourceSample(now);

		using var doc = JsonDocument.Parse(output.Trim());
		var root = doc.RootElement;

		return new ResourceSample(now)
		{
			CpuPercent = ParsePercent(root, "CPUPerc"),
			WorkingSetBytes = ParseMemoryUsage(root)
		};
	}

	public Task StopAsync(CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}

	private static double? ParsePercent(JsonElement root, string field)
	{
		if (!root.TryGetProperty(field, out var element)) return null;
		var text = element.GetString()?.TrimEnd('%');
		return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
	}

	private static long? ParseMemoryUsage(JsonElement root)
	{
		if (!root.TryGetProperty("MemUsage", out var element)) return null;
		var used = element.GetString()?.Split('/').FirstOrDefault()?.Trim();
		return used is null ? null : ParseByteSize(used);
	}

	private static long? ParseByteSize(string text)
	{
		var units = new (string Suffix, double Multiplier)[]
		{
			("GiB", 1024.0 * 1024 * 1024), ("MiB", 1024.0 * 1024), ("KiB", 1024.0), ("B", 1.0)
		};

		foreach (var (suffix, multiplier) in units)
		{
			if (!text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
			var number = text[..^suffix.Length].Trim();
			if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
				return (long)(value * multiplier);
		}

		return null;
	}
}

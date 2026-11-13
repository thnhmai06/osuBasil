using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Basil.Application.Diagnostics.Services;
using Basil.Infrastructure.Diagnostics.Services;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Infrastructure.Diagnostics.Tests.Services;

/// <summary>The sampler keeps one window per known host label, whatever labels the meter is given.</summary>
public sealed class RuntimeSamplerTests
{
	[Fact]
	public void UnknownHostLabels_CountedAsOther()
	{
		using var sampler = new RuntimeSampler(new FakeTimeProvider());
		using var meter = new Meter("Basil.Test");
		var duration = meter.CreateHistogram<double>(DiagnosticsMetrics.HttpDurationName);
		for (var i = 0; i < 1000; i++)
			duration.Record(1, new KeyValuePair<string, object?>(DiagnosticsMetrics.HostTag, $"random-{i}"),
				new KeyValuePair<string, object?>(DiagnosticsMetrics.StatusTag, "2xx"));

		var http = (IDictionary<string, object>)sampler.Sample()["http"];

		Assert.Equal([HostLabels.Other], http.Keys);
	}

	[Theory]
	[InlineData("stable")]
	[InlineData("irc")]
	public void RecordedDurations_PercentilesRiseInOrder(string category)
	{
		using var sampler = new RuntimeSampler(new FakeTimeProvider());
		var duration = category == "stable" ? DiagnosticsMetrics.StableDuration : DiagnosticsMetrics.IrcDuration;
		for (var i = 1; i <= 100; i++) duration.Record(i);

		var window = Sampled(sampler, category);

		var (p50, p95, p99) = (Number(window, "p50Milliseconds"), Number(window, "p95Milliseconds"),
			Number(window, "p99Milliseconds"));
		Assert.InRange(p50, 1, 100);
		Assert.True(p50 <= p95 && p95 <= p99 && p99 <= 100);
	}

	[Fact]
	public void HttpWindow_HasP99()
	{
		using var sampler = new RuntimeSampler(new FakeTimeProvider());
		using var meter = new Meter("Basil.Test");
		var duration = meter.CreateHistogram<double>(DiagnosticsMetrics.HttpDurationName);
		for (var i = 1; i <= 100; i++)
			duration.Record(i, new KeyValuePair<string, object?>(DiagnosticsMetrics.HostTag, HostLabels.Other),
				new KeyValuePair<string, object?>(DiagnosticsMetrics.StatusTag, "2xx"));

		var window = Sampled(sampler, "http")[HostLabels.Other]!;

		Assert.True(Number(window, "p50Milliseconds") <= Number(window, "p99Milliseconds"));
	}

	[Theory]
	[InlineData("totalMemoryBytes")]
	[InlineData("usedMemoryBytes")]
	[InlineData("networkSentBytesPerSecond")]
	[InlineData("networkReceivedBytesPerSecond")]
	public void MachineCategory_HasNonNegative(string key)
	{
		using var sampler = new RuntimeSampler(new FakeTimeProvider());

		var machine = Sampled(sampler, "machine");

		Assert.True(Number(machine, key) >= 0);
	}

	[Fact]
	public void MachineCategory_UsedMemoryWithinTotal()
	{
		using var sampler = new RuntimeSampler(new FakeTimeProvider());

		var machine = Sampled(sampler, "machine");

		Assert.True(Number(machine, "usedMemoryBytes") <= Number(machine, "totalMemoryBytes"));
	}

	private static JsonNode Sampled(RuntimeSampler sampler, string category)
	{
		return JsonSerializer.SerializeToNode(sampler.Sample())![category]!;
	}

	private static double Number(JsonNode node, string key)
	{
		return node[key]!.GetValue<double>();
	}
}
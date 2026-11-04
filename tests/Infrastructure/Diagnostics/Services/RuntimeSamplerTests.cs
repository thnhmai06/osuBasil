using System.Diagnostics.Metrics;
using Basil.Application.Common.Diagnostics;
using Basil.Infrastructure.Diagnostics.Services;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Infrastructure.Diagnostics.Tests.Services;

/// <summary>The sampler keeps one window per known host label, whatever labels the meter is given.</summary>
public sealed class RuntimeSamplerTests
{
	[Fact]
	public void Unknown_host_labels_are_counted_as_other()
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
}

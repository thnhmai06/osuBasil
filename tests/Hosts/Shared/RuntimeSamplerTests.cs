using Basil.Host.Shared.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Host.Shared.Tests;

/// <summary>The sampler keeps one window per known host label, whatever labels the meter is given.</summary>
public sealed class RuntimeSamplerTests
{
	[Fact]
	public void Unknown_host_labels_are_counted_as_other()
	{
		using var sampler = new RuntimeSampler(new FakeTimeProvider());
		for (var i = 0; i < 1000; i++)
			BasilMeter.HttpDuration.Record(1, new KeyValuePair<string, object?>("host", $"random-{i}"),
				new KeyValuePair<string, object?>("status", "2xx"));

		var http = (IDictionary<string, object>)sampler.Sample()["http"];

		Assert.Equal([HostLabels.Other], http.Keys);
	}
}

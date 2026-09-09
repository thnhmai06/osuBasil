using Basil.Server.Features.Diagnostics;

namespace Basil.Server.Tests.Features.Diagnostics;

/// <summary>Verifies <see cref="ExceptionsSampler" /> reads the process's cumulative thrown-exception count.</summary>
public class ExceptionsSamplerTests
{
	[Fact]
	public void SampleReportsTheMeterListenersCumulativeCount()
	{
		var listener = new RuntimeMeterListener();
		listener.RecordForTest("dotnet.exceptions", 1, [new KeyValuePair<string, object?>("error.type", "A")]);
		listener.RecordForTest("dotnet.exceptions", 1, [new KeyValuePair<string, object?>("error.type", "B")]);
		var sampler = new ExceptionsSampler(listener);

		var sample = sampler.Sample();

		Assert.Equal(2, sample.TotalThrown);
	}
}

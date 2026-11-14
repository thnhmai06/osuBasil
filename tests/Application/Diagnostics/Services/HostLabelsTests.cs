using Basil.Application.Diagnostics.Services;

namespace Basil.Application.Diagnostics.Tests.Services;

/// <summary>The Host header never becomes a new metric label.</summary>
public sealed class HostLabelsTests
{
	private const string Domain = "basil.test";

	[Theory]
	[InlineData("api.basil.test", "api")]
	[InlineData("API.Basil.Test", "api")]
	[InlineData("osu.basil.test", "osu")]
	[InlineData("c.basil.test", "c")]
	[InlineData("assets.basil.test", "assets")]
	[InlineData("c6.basil.test", "c6")]
	[InlineData("irc.basil.test", HostLabels.Other)]
	[InlineData("basil.test", HostLabels.Domain)]
	[InlineData("evil.basil.test", HostLabels.Other)]
	[InlineData("api.evil.test", HostLabels.Other)]
	[InlineData("xapi.basil.test", HostLabels.Other)]
	[InlineData("", HostLabels.Other)]
	public void KnownHost_MapsToLabel(string host, string expected)
	{
		Assert.Equal(expected, HostLabels.Resolve(host, Domain));
	}

	[Fact]
	public void ManyRandomHosts_KnownLabelsOnly()
	{
		var random = new Random(1);
		var labels = Enumerable.Range(0, 1000).Select(_ =>
			HostLabels.Resolve($"{Guid.NewGuid():N}{random.Next()}.{(random.Next(2) == 0 ? Domain : "x.test")}",
				Domain)).ToHashSet();

		Assert.True(labels.Count <= HostLabels.All.Count);
		Assert.All(labels, label => Assert.Contains(label, HostLabels.All));
	}
}
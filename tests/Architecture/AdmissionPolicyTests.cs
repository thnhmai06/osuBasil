using Basil.Gateway;

namespace Basil.ArchitectureTests;

/// <summary>
///     Every entry point declares an admission policy, by carrying <see cref="AdmissionPolicyAttribute" /> in its
///     metadata. Only the rule exists today: the hosts do not enumerate their entry points as metadata yet, so they
///     are listed as known-undeclared until they do.
/// </summary>
public sealed class AdmissionPolicyTests
{
	private sealed record EntryPoint(string Host, string Name, IReadOnlyList<object> Metadata);

	// target: phase 7 (each host declares a policy per route, packet type and command, and leaves this list)
	private static readonly string[] KnownUndeclaredHosts = ["Rest", "Stable", "Irc"];

	private static IEnumerable<EntryPoint> Undeclared(IEnumerable<EntryPoint> entryPoints)
	{
		return entryPoints.Where(entry => !entry.Metadata.OfType<AdmissionPolicyAttribute>().Any());
	}

	[Fact]
	public void AnEntryPointWithoutAPolicy_IsDetected()
	{
		var entryPoints = new[]
		{
			new EntryPoint("Rest", "GET /fake", []),
			new EntryPoint("Rest", "GET /declared", [new AdmissionPolicyAttribute()])
		};

		Assert.Equal(["GET /fake"], Undeclared(entryPoints).Select(entry => entry.Name));
	}

	[Fact]
	public void NoHostOutsideTheKnownUndeclaredListHasAnEntryPointWithoutAPolicy()
	{
		// Hosts that enumerate their entry points are added here; the others are in KnownUndeclaredHosts.
		var enumerated = Array.Empty<EntryPoint>();

		Assert.Empty(Undeclared(enumerated).Where(entry => !KnownUndeclaredHosts.Contains(entry.Host)));
	}
}

using Basil.Domain;
using NetArchTest.Rules;

namespace Basil.ArchitectureTests;

/// <summary>
///     Enforces the namespace-boundary rule inside <c>Basil.Domain</c>: a namespace may only
///     reference the namespaces declared in <see cref="DomainAdjacency" />.
/// </summary>
/// <remarks>
///     <see cref="SliceBoundaryTests" /> stops covering a type the moment it leaves
///     <c>Basil.Server.Features.&lt;Slice&gt;</c>, so nothing watched <c>Basil.Domain</c> before this
///     rule existed. It exists ahead of Task C1, which moves ninety-six files in there, so every
///     feature's move is checked as it arrives instead of all at once at the end.
/// </remarks>
public class DomainBoundaryTests
{
	private const string DomainPrefix = "Basil.Domain.";

	[Fact]
	public void Namespaces_Should_Only_Reference_Declared_Namespaces()
	{
		var assembly = typeof(AssemblyMarker).Assembly;
		var allNamespaces = Types.InAssembly(assembly).GetTypes()
			.Where(t => t.Namespace?.StartsWith(DomainPrefix, StringComparison.Ordinal) == true)
			.Select(t => t.Namespace![DomainPrefix.Length..].Split('.')[0])
			.Distinct()
			.ToArray();

		var violations = new List<string>();

		foreach (var type in Types.InAssembly(assembly).GetTypes()
			         .Where(t => t.Namespace?.StartsWith(DomainPrefix, StringComparison.Ordinal) == true))
		{
			var from = type.Namespace![DomainPrefix.Length..].Split('.')[0];
			var forbidden = OtherNamespaces(allNamespaces, from, DomainAdjacency.Allowed);
			if (forbidden.Length == 0) continue;

			var result = Types.InAssembly(assembly)
				.That().HaveName(type.Name).And().ResideInNamespace(type.Namespace)
				.Should().NotHaveDependencyOnAny(forbidden)
				.GetResult();

			if (!result.IsSuccessful)
				violations.Add(
					$"{type.FullName} -> one of [{string.Join(", ", forbidden)}] " +
					$"(via {string.Join(", ", result.FailingTypeNames ?? [])})");
		}

		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
	}

	/// <summary>Returns <c>Basil.Domain.&lt;X&gt;</c> for every namespace that is neither <paramref name="from" /> nor an allowed target of <paramref name="from" />.</summary>
	private static string[] OtherNamespaces(string[] allNamespaces, string from, (string From, string To)[] allowed)
	{
		var allowedTargets = allowed.Where(edge => edge.From == from).Select(edge => edge.To).ToHashSet();
		return allNamespaces
			.Where(ns => ns != from && !allowedTargets.Contains(ns))
			.Select(ns => $"{DomainPrefix}{ns}")
			.ToArray();
	}
}
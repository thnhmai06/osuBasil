using System.Diagnostics.Metrics;
using System.Reflection;
using Basil.Application.Common.Diagnostics;
using Basil.ArchitectureTests.Handlers;
using Mediator;

namespace Basil.ArchitectureTests;

/// <summary>
///     Every module that handles messages takes part in the diagnostics: its Infrastructure project owns a meter named
///     <c>Basil.&lt;Module&gt;</c> and answers <see cref="CollectDiagnostics" /> with a handler.
/// </summary>
public class DiagnosticsContributionTests
{
	private const BindingFlags Declared = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
	                                      BindingFlags.DeclaredOnly;

	private static IEnumerable<Assembly> ModuleInfrastructures()
	{
		return ModuleAssemblies.Infrastructures.Where(assembly =>
			assembly.GetName().Name != "Basil.Infrastructure.Common");
	}

	[Fact]
	public void EveryModule_OwnsMeter()
	{
		var missing = ModuleInfrastructures().SelectMany(assembly => Missing(assembly.GetName().Name!.Split('.')[2],
			assembly.GetTypes()));

		Assert.Empty(missing);
	}

	[Fact]
	public void InspectsModules_PositiveControl()
	{
		var inspected = ModuleInfrastructures().Where(assembly => HasHandlers(assembly.GetTypes())).ToList();

		Assert.True(inspected.Count >= 7, $"Only {inspected.Count} modules have handlers.");
		Assert.Contains(inspected, assembly => assembly.GetName().Name == "Basil.Infrastructure.Diagnostics");
	}

	[Fact]
	public void ReportsMissing_PositiveControl()
	{
		Assert.Equal(new[] { "Fake has no meter named Basil.Fake", "Fake has no handler of CollectDiagnostics" },
			Missing("Fake", [typeof(OnlyAHandlerOfSomethingElse)]).ToArray());
		Assert.Equal(new[] { "Fake has no handler of CollectDiagnostics" },
			Missing("Fake", [typeof(OnlyAHandlerOfSomethingElse), typeof(FakeMeter)]).ToArray());
		Assert.Empty(Missing("Fake", [typeof(FakeMeter), typeof(Answering)]));
		Assert.Empty(Missing("Fake", [typeof(FakeMeter)]));
	}

	private static IEnumerable<string> Missing(string module, Type[] types)
	{
		if (!HasHandlers(types)) yield break;

		var meter = $"Basil.{module}";
		if (!types.Any(type => type.GetFields(Declared).Any(field =>
			    field.FieldType == typeof(Meter) && ((Meter?)field.GetValue(null))?.Name == meter)))
			yield return $"{module} has no meter named {meter}";

		if (!types.Any(type => type.GetInterfaces().Contains(typeof(INotificationHandler<CollectDiagnostics>))))
			yield return $"{module} has no handler of {nameof(CollectDiagnostics)}";
	}

	private static bool HasHandlers(Type[] types)
	{
		return types.Any(type => type.Namespace?.EndsWith(".Handlers", StringComparison.Ordinal) == true);
	}

	private static class FakeMeter
	{
		internal static readonly Meter Instance = new("Basil.Fake");
	}
}
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Fakes.Basil.Application.Fake.Capabilities;

namespace Basil.ArchitectureTests;

/// <summary>
///     A capability (<c>Scoped*</c> view) serves one request and is then dropped, so no class or struct may keep one in a
///     field or property: a kept capability would let its rights outlive a change of role or restriction.
/// </summary>
public class ScopedLifetimeTests
{
	private const string CapabilityNamespace = @"(^|\.)Basil\.Application\.\w+\.Capabilities";

	private static readonly string[] OtherAssemblies =
	[
		"Basil", "Basil.Host.Stable", "Basil.Host.Irc", "Basil.Host.Rest", "Basil.Host.Cli", "Basil.Host.Shared"
	];

	[Fact]
	public void NoTypeKeepsACapabilityInAFieldOrProperty()
	{
		var assemblies = ModuleAssemblies.Applications.Concat(ModuleAssemblies.Infrastructures)
			.Concat(OtherAssemblies.Select(Assembly.Load));

		var offenders = KeepersOfCapabilities(assemblies.SelectMany(assembly => assembly.GetTypes())).ToList();

		Assert.True(offenders.Count == 0, "Types that keep a capability: " + string.Join(", ", offenders));
	}

	[Fact]
	public void TheDetectorSeesATypeThatKeepsACapability_PositiveControl()
	{
		var offenders = KeepersOfCapabilities([typeof(Holder)]).ToList();

		Assert.Contains($"{nameof(Holder)}.{nameof(Holder.Kept)}", offenders);
	}

	private static IEnumerable<string> KeepersOfCapabilities(IEnumerable<Type> types)
	{
		const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
		                              BindingFlags.DeclaredOnly;

		foreach (var type in types.Where(type => !IsCompilerGenerated(type) && !IsCapability(type)))
		{
			foreach (var field in type.GetFields(Instance).Where(field => !IsCompilerGenerated(field)))
				if (IsCapability(field.FieldType))
					yield return $"{type.Name}.{field.Name}";

			foreach (var property in type.GetProperties(Instance))
				if (IsCapability(property.PropertyType))
					yield return $"{type.Name}.{property.Name}";
		}
	}

	private static bool IsCapability(Type type)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		return type.Namespace is { } ns && Regex.IsMatch(ns, CapabilityNamespace) &&
		       type.Name.StartsWith("Scoped", StringComparison.Ordinal);
	}

	private static bool IsCompilerGenerated(MemberInfo member)
	{
		for (var current = member; current is not null; current = current.DeclaringType)
			if (current.IsDefined(typeof(CompilerGeneratedAttribute), false))
				return true;

		return false;
	}

	private sealed class Holder
	{
		public ScopedFake Kept;
	}
}
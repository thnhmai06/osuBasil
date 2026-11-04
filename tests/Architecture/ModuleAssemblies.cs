using System.Reflection;

namespace Basil.ArchitectureTests;

/// <summary>The assemblies of the module projects, loaded by name so a test can measure each of them.</summary>
internal static class ModuleAssemblies
{
	internal static readonly string[] Modules =
		["Anticheat", "Authentication", "Beatmaps", "Chat", "Content", "Multiplayer", "Scores", "Servers", "Users"];

	/// <summary>Gets the Application assembly of every module.</summary>
	internal static IReadOnlyList<Assembly> Applications { get; } =
		Modules.Select(module => Assembly.Load($"Basil.Application.{module}")).ToArray();

	/// <summary>Gets the Infrastructure assembly of every module that has one, and the shared one.</summary>
	internal static IReadOnlyList<Assembly> Infrastructures { get; } =
		Modules.Select(module => Assembly.Load($"Basil.Infrastructure.{module}"))
			.Append(Assembly.Load("Basil.Infrastructure.Common")).ToArray();
}
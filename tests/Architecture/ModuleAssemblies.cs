using System.Reflection;

namespace Basil.ArchitectureTests;

internal static class ModuleAssemblies
{
	internal static readonly string[] Modules =
	[
		"Anticheat", "Authentication", "Beatmaps", "Bot", "Chat", "Content", "Diagnostics", "Multiplayer", "Scores",
		"Servers", "Users"
	];

	internal static IReadOnlyList<Assembly> Applications { get; } =
		Modules.Select(module => Assembly.Load($"Basil.Application.{module}")).ToArray();

	internal static IReadOnlyList<Assembly> Infrastructures { get; } =
		Modules.Select(module => Assembly.Load($"Basil.Infrastructure.{module}"))
			.Append(Assembly.Load("Basil.Infrastructure.Basil.Shared")).ToArray();
}
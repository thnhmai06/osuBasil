using System.Reflection;

namespace Basil.Server.Host;

/// <summary>The version of the running server, as compiled into the assembly.</summary>
internal static class BuildVersion
{
	/// <summary>
	///     The informational version, falling back to the assembly version and then to
	///     <c>unknown</c> when neither is present.
	/// </summary>
	public static string Informational { get; } = Resolve();

	private static string Resolve()
	{
		var assembly = typeof(BuildVersion).Assembly;
		return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
		       ?? assembly.GetName().Version?.ToString()
		       ?? "unknown";
	}
}

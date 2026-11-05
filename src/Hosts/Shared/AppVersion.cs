using System.Reflection;

namespace Basil.Host.Shared;

/// <summary>The version of the running Basil application.</summary>
public static class AppVersion
{
	/// <summary>Gets the version, or <c>unknown</c> when the executable does not carry one.</summary>
	public static string Current => Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
}
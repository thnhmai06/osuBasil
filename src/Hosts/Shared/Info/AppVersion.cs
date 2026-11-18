using System.Reflection;

namespace Basil.Host.Shared.Info;

public static class AppVersion
{
	public static string Current => Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
}
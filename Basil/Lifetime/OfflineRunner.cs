using Basil.Host.Cli;

namespace Basil.Lifetime;

public static class OfflineRunner
{
	public static bool Handles(string[] args)
	{
		return CommandLineArguments.IsCommand(args);
	}

	public static Task<int> RunAsync(string[] args, Action<WebApplicationBuilder>? configure = null)
	{
		return ShellRunner.RunAsync(args, configure);
	}
}
using Basil.Server.Shared.Configuration;
using Microsoft.Extensions.Configuration;

namespace Basil.Server.Host;

/// <summary>
///     The commands the server executable accepts instead of starting the server.
/// </summary>
/// <remarks>
///     Each command runs on its own and then the process ends; none of them starts a web host. Any
///     argument that is not one of these is left for configuration binding, so settings can still be
///     overridden on the command line.
/// </remarks>
internal static class CommandLine
{
	private static readonly string[] UpdateFlags = ["--update", "-u"];
	private static readonly string[] VersionFlags = ["--version", "-v"];
	private static readonly string[] HelpFlags = ["--help", "-h"];

	/// <summary>Runs the command named in <paramref name="args" />, if there is one.</summary>
	/// <param name="args">The process arguments.</param>
	/// <returns>
	///     <see langword="true" /> when a command ran and the process should end without starting the
	///     server; <see langword="false" /> when the arguments name no command.
	/// </returns>
	public static async Task<bool> TryRunAsync(string[] args)
	{
		if (Matches(args, HelpFlags))
		{
			WriteHelp();
			return true;
		}

		if (Matches(args, VersionFlags))
		{
			WriteVersion();
			return true;
		}

		if (!Matches(args, UpdateFlags)) return false;

		await UpdateAsync(args);
		return true;
	}

	private static bool Matches(string[] args, string[] flags)
	{
		return args.Any(argument => flags.Contains(argument, StringComparer.OrdinalIgnoreCase));
	}

	private static void WriteVersion()
	{
		Console.WriteLine($"Basil {BuildVersion.Informational}");
		Console.WriteLine($"Running on {Environment.Version} ({Environment.OSVersion})");
	}

	private static void WriteHelp()
	{
		Console.WriteLine($"Basil {BuildVersion.Informational}");
		Console.WriteLine();
		Console.WriteLine("Usage: Basil.Server [command]");
		Console.WriteLine();
		Console.WriteLine("Commands:");
		Console.WriteLine("  -u, --update    Check for a newer release, install it, and restart.");
		Console.WriteLine("  -v, --version   Show the version of this server.");
		Console.WriteLine("  -h, --help      Show this list.");
		Console.WriteLine();
		Console.WriteLine("With no command, the server starts.");
		Console.WriteLine("Settings come from Data/appsettings.json; see docs/for-technicians/configuration.md.");
	}

	private static async Task UpdateAsync(string[] args)
	{
		var options = ReadUpdateCheckOptions(args);
		var probe = new VelopackUpdateProbe(options);

		Console.WriteLine($"Basil {BuildVersion.Informational}, checking {options.Source}");

		var result = await probe.CheckAsync(CancellationToken.None);
		switch (result.Outcome)
		{
			case UpdateCheckOutcome.UpToDate:
				Console.WriteLine("Already up to date.");
				return;
			case UpdateCheckOutcome.Failed:
				Console.Error.WriteLine("Could not reach the release feed. Nothing was changed.");
				return;
			case UpdateCheckOutcome.NotInstallable:
				Console.Error.WriteLine(
					"This copy of Basil was not installed by the updater, so it cannot update itself.");
				return;
		}

		Console.WriteLine($"Installing {result.AvailableVersion}...");
		await probe.ApplyAsync(CancellationToken.None);
	}

	private static UpdateCheckOptions ReadUpdateCheckOptions(string[] args)
	{
		var configuration = new ConfigurationBuilder();
		ConfigurationSetup.AddSources(configuration,
			Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production",
			args.Where(argument => !argument.StartsWith('-')).ToArray());

		return configuration.Build().GetSection(UpdateCheckOptions.SectionName).Get<UpdateCheckOptions>() ?? new UpdateCheckOptions();
	}
}

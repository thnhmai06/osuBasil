using System.Globalization;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;

namespace Basil.Server.Host;

/// <summary>Logs the startup banner for the host.</summary>
internal static class StartupBanner
{
	private const string BasilDescription =
		"A lightweight, high-performance osu! server for tournaments and multiplayer.";

	private const string BasilLicense = "Copyright © 2026 Mai Thành. Licensed under the MIT License.";

	private const string BasilArt =
		"""
		                    _
		                  _(_)_          ____            _wW̲w   _
		      @@@@       (_)@(_)   vVVVv| __ )  __ _ ___(_) |) _(_)_
		     @@()@@ wWWWw  (_)\    (___)|  _ \ / _` / __| | | (_)@(_)
		      @@@@  (___)     `|/    Y  | |_) | (_| \__ \ | |   (_)\
		       /      Y       \|    \|/ |____/ \__,_|___/_|_|      |
		    \ |     \ |/       | / \ | /  \|/       |/    \|      \|/
		    \\|//   \\|///  \\\|//\\\|/// \|///  \\\|//  \\|//  \\\|//
		^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
		"""; // original art by Joan G. Stark (Spunk), edited by Mai Thành (thnhmai06)

	/// <summary>Logs the startup banner: ASCII art, version, and host machine/hardware info.</summary>
	/// <param name="app">The built application whose logger is used.</param>
	public static void Log(WebApplication app)
	{
		var logger = app.Services.GetRequiredService<ILogger<Bootstrap>>();

		var assembly = typeof(Bootstrap).Assembly;
		var version =
			assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
				?.InformationalVersion
			?? assembly.GetName().Version?.ToString()
			?? "unknown";
		var gcInfo = GC.GetGCMemoryInfo();
		var appDrive = new DriveInfo(Path.GetPathRoot(AppContext.BaseDirectory)!);

		LogLine(logger);

		foreach (var line in BasilArt.Split('\n')) logger.LogInformation("{Art}", line.TrimEnd('\r'));
		logger.LogInformation("Basil v{Version}", version);
		logger.LogInformation("{Description}", BasilDescription);
		logger.LogInformation("{License}", BasilLicense);
		logger.LogInformation("Current time (UTC): {Time}", DateTimeOffset.UtcNow.ToString("O"));
		logger.LogInformation(""); // \n

		LogSection(logger, "Environment");
		LogValue(logger, "Machine", Environment.MachineName);
		LogValue(logger, "OS", RuntimeInformation.OSDescription);
		LogValue(logger, "OS Version", Environment.OSVersion.Version);
		LogValue(logger, "OS Architecture", RuntimeInformation.OSArchitecture);
		LogValue(logger, "Runtime", RuntimeInformation.FrameworkDescription);
		LogValue(logger, "Runtime Identifier", RuntimeInformation.RuntimeIdentifier);
		LogValue(logger, "CLR", Environment.Version);
		LogValue(logger, "Process Architecture", RuntimeInformation.ProcessArchitecture);
		LogValue(logger, "Process ID", Environment.ProcessId);
		LogValue(logger, "Process Name",
			Environment.ProcessPath is { } path
				? Path.GetFileNameWithoutExtension(path)
				: "Unknown");
		LogValue(logger, "Logical CPUs", Environment.ProcessorCount);
		LogValue(logger, "64-bit OS", Environment.Is64BitOperatingSystem);
		LogValue(logger, "64-bit Process", Environment.Is64BitProcess);

		LogSection(logger, "GC");
		LogValue(logger, "Server GC", GCSettings.IsServerGC);
		LogValue(logger, "Latency Mode", GCSettings.LatencyMode);
		LogValue(logger, "Memory Limit", $"{gcInfo.TotalAvailableMemoryBytes / 1024d / 1024 / 1024:F2} GiB");
		LogValue(logger, "Heap Size", $"{gcInfo.HeapSizeBytes / 1024d / 1024:F2} MiB");

		LogSection(logger, "Localization");
		LogValue(logger, "Time Zone", TimeZoneInfo.Local.DisplayName);
		LogValue(logger, "Time Zone ID", TimeZoneInfo.Local.Id);
		LogValue(logger, "Culture", CultureInfo.CurrentCulture.Name);
		LogValue(logger, "UI Culture", CultureInfo.CurrentUICulture.Name);

		LogSection(logger, "Storage");
		LogValue(logger, "Application Drive", appDrive.Name);
		LogValue(logger, "Drive Format", appDrive.DriveFormat);
		LogValue(logger, "Total Space", $"{appDrive.TotalSize / 1024d / 1024 / 1024:F2} GiB");
		LogValue(logger, "Free Space", $"{appDrive.AvailableFreeSpace / 1024d / 1024 / 1024:F2} GiB");

		LogSection(logger, "Paths");
		LogValue(logger, "Working Directory", Environment.CurrentDirectory);
		LogValue(logger, "Base Directory", AppContext.BaseDirectory);
		LogValue(logger, "Command Line", Environment.CommandLine);

		LogLine(logger);
		return;

		static void LogLine(ILogger logger)
		{
			logger.LogInformation(
				"────────────────────────────────────────────────────────────────");
		}

		static void LogSection(ILogger logger, string name)
		{
			logger.LogInformation("{Section}:", name);
		}

		static void LogValue(ILogger logger, string key, object? value)
		{
			logger.LogInformation("\t{Key,-22} : {Value}", key, value);
		}
	}
}

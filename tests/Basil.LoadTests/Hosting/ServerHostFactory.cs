using Basil.LoadTests.Configuration;

namespace Basil.LoadTests.Hosting;

/// <summary>
///     Builds the <see cref="IServerHost" /> a profile asks for. This is the only place a scenario's
///     choice of host kind is resolved — everything else in <c>Scenarios/</c> only ever sees <see cref="IServerHost" />.
/// </summary>
public static class ServerHostFactory
{
	/// <summary>Creates the host implementation for <paramref name="settings" />.<see cref="ServerHostSettings.Kind" />.</summary>
	/// <param name="settings">The resolved server-host settings from the active profile.</param>
	/// <param name="countersSettings">Settings for the in-process .NET runtime counters collector.</param>
	/// <param name="logWarning">Sink for non-fatal degradation warnings (e.g. a failed counters attach).</param>
	public static IServerHost Create(ServerHostSettings settings, DotnetCountersSettings countersSettings,
		Action<string> logWarning)
	{
		return settings.Kind switch
		{
			ServerHostKind.Dotnet => new DotnetServerHost(settings, countersSettings, logWarning),
			ServerHostKind.Docker => new DockerServerHost(settings),
			ServerHostKind.Existing => new ExistingServerHost(settings, countersSettings, logWarning),
			_ => throw new ArgumentOutOfRangeException(nameof(settings), settings.Kind, "Unknown server host kind.")
		};
	}
}
namespace Basil.LoadTests.Configuration;

/// <summary>Which <see cref="Hosting.IServerHost" /> implementation a profile targets.</summary>
public enum ServerHostKind
{
	/// <summary>A local <c>dotnet run</c> or published binary, spawned and torn down by the harness.</summary>
	Dotnet,

	/// <summary>A Docker Compose service, spawned and torn down by the harness.</summary>
	Docker,

	/// <summary>An already-running instance the harness only connects to.</summary>
	Existing
}

/// <summary>How the server is reached and, for owned hosts, how it is started and stopped.</summary>
public sealed class ServerHostSettings
{
	/// <summary>Which host implementation to use.</summary>
	public ServerHostKind Kind { get; init; } = ServerHostKind.Dotnet;

	/// <summary>The FQDN the server is configured with (bancho hosts are <c>c.{Domain}</c>, etc.).</summary>
	public required string Domain { get; init; }

	/// <summary>The HTTPS port the server listens on.</summary>
	public int Port { get; init; } = 8443;

	/// <summary>The TCP port the embedded IRC gateway listens on.</summary>
	public int IrcPort { get; init; } = 16667;

	/// <summary>Path to the TLS certificate the server should load.</summary>
	public required string CertPath { get; init; }

	/// <summary>Password for <see cref="CertPath" />.</summary>
	public required string CertPassword { get; init; }

	/// <summary>How long to wait for the server to answer before treating startup as failed.</summary>
	public int StartupTimeoutSeconds { get; init; } = 120;

	/// <summary>How long to let a freshly started server sit idle before the first metrics sample.</summary>
	public int IdleSettleSeconds { get; init; } = 30;

	/// <summary>Settings for <see cref="ServerHostKind.Dotnet" />.</summary>
	public DotnetHostSettings Dotnet { get; init; } = new();

	/// <summary>Settings for <see cref="ServerHostKind.Docker" />.</summary>
	public DockerHostSettings Docker { get; init; } = new();

	/// <summary>Settings for <see cref="ServerHostKind.Existing" />.</summary>
	public ExistingHostSettings Existing { get; init; } = new();

	/// <summary>Gets <see cref="StartupTimeoutSeconds" /> as a <see cref="TimeSpan" />.</summary>
	public TimeSpan StartupTimeout => TimeSpan.FromSeconds(StartupTimeoutSeconds);

	/// <summary>Gets <see cref="IdleSettleSeconds" /> as a <see cref="TimeSpan" />.</summary>
	public TimeSpan IdleSettle => TimeSpan.FromSeconds(IdleSettleSeconds);
}

/// <summary>How a locally launched server process is started.</summary>
public enum DotnetLaunchMode
{
	/// <summary><c>dotnet run --project src/Basil.Web</c>. Slower to start, no publish step.</summary>
	Run,

	/// <summary>A pre-published binary under <see cref="DotnetHostSettings.PublishDirectory" />.</summary>
	Published
}

/// <summary>Settings for launching Basil.Web as a local child process.</summary>
public sealed class DotnetHostSettings
{
	/// <summary>Whether to run from source or from a published binary.</summary>
	public DotnetLaunchMode Mode { get; init; } = DotnetLaunchMode.Published;

	/// <summary>Directory a published build is placed in / read from.</summary>
	public string PublishDirectory { get; init; } = ".loadtest/server";

	/// <summary>Whether a missing publish output should be built automatically before the first run.</summary>
	public bool AutoPublish { get; init; } = true;
}

/// <summary>Settings for launching Basil via <c>docker compose</c>.</summary>
public sealed class DockerHostSettings
{
	/// <summary>Path to the compose file (defaults to the repo root's <c>docker-compose.yml</c>).</summary>
	public string ComposeFile { get; init; } = "docker-compose.yml";

	/// <summary>The compose service name to start and observe.</summary>
	public string ServiceName { get; init; } = "basil";
}

/// <summary>Settings for attaching to a server the harness does not own.</summary>
public sealed class ExistingHostSettings
{
	/// <summary>The IP address to actually connect to (the URI host is only used for TLS SNI / the HTTP Host header).</summary>
	public string HostAddress { get; init; } = "127.0.0.1";

	/// <summary>
	///     The local process id of the running server, if known. Enables process/counters metrics; when
	///     absent, those metrics are reported as unavailable rather than guessed at.
	/// </summary>
	public int? ProcessId { get; init; }
}

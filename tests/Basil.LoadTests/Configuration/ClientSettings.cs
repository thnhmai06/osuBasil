namespace Basil.LoadTests.Configuration;

/// <summary>How virtual users share (or don't share) HTTP connections to the server.</summary>
public enum ConnectionMode : byte
{
	/// <summary>One pooled connection set shared by every virtual user. Maximum generator throughput.</summary>
	Shared,

	/// <summary>One connection per virtual user. Answers the connection-scalability question directly.</summary>
	PerUser
}

/// <summary>Settings controlling how the load generator's HTTP client behaves.</summary>
public sealed class ClientSettings
{
	/// <summary>The HTTP protocol version to use. The real osu! client speaks 1.1.</summary>
	public string HttpVersion { get; init; } = "1.1";

	/// <summary>Whether connections are pooled and shared, or one per virtual user.</summary>
	public ConnectionMode ConnectionMode { get; init; } = ConnectionMode.Shared;

	/// <summary>The maximum number of pooled connections per server, when <see cref="ConnectionMode.Shared" />.</summary>
	public int MaxConnectionsPerServer { get; init; } = 256;

	/// <summary>The per-request timeout.</summary>
	public int RequestTimeoutSeconds { get; init; } = 30;

	/// <summary>
	///     How often an idle bancho client polls to stay alive. Must stay well under the server's
	///     300-second ghost-session reaper interval.
	/// </summary>
	public int PollIntervalSeconds { get; init; } = 60;

	/// <summary>Gets <see cref="RequestTimeoutSeconds" /> as a <see cref="TimeSpan" />.</summary>
	public TimeSpan RequestTimeout => TimeSpan.FromSeconds(RequestTimeoutSeconds);

	/// <summary>Gets <see cref="PollIntervalSeconds" /> as a <see cref="TimeSpan" />.</summary>
	public TimeSpan PollInterval => TimeSpan.FromSeconds(PollIntervalSeconds);
}

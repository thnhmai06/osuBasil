using Basil.Tests.Shared.Clients;

namespace Basil.Tests.LoadTests;

/// <summary>Parsed command-line options for running a load test, seeding, or running the control probe.</summary>
public sealed record LoadTestOptions
{
	/// <summary>The name of the environment variable that holds the owner password for <c>seed</c>.</summary>
	public const string OwnerPasswordVariable = "BASIL_OWNER_PASSWORD";

	public string Test { get; init; } = "load";

	public string Connect { get; init; } = "192.168.27.124";

	public int Port { get; init; } = 8443;

	public int IrcPort { get; init; } = 6697;

	public string Domain { get; init; } = "basil.local";

	public string AccountsPath { get; init; } = "accounts.csv";

	public string WorldPath { get; init; } = "world-data.json";

	public string ReportsRoot { get; init; } = "reports";

	public int TargetSessions { get; init; } = 100;

	public int StepDelta { get; init; } = 50;

	public int StepCount { get; init; } = 6;

	public int StepMinutes { get; init; } = 5;

	public int BasilCores { get; init; } = 4;

	public int Seed { get; init; } = 20261125;

	public TimeSpan SteadyDuration { get; init; } = TimeSpan.FromMinutes(10);

	public TimeSpan SoakDuration { get; init; } = TimeSpan.FromHours(6);

	public TimeSpan RoundDuration { get; init; } = TimeSpan.FromSeconds(12);

	public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

	public bool Pilot { get; init; }

	// Seed-only options.
	public string OwnerUser { get; init; } = "admin";

	/// <summary>Gets the owner password; it comes from the <c>BASIL_OWNER_PASSWORD</c> environment variable, never from the command line, so it stays out of process lists and shell history.</summary>
	public string OwnerPassword { get; init; } = Environment.GetEnvironmentVariable(OwnerPasswordVariable) ?? string.Empty;

	public int PlayerCount { get; init; } = 1500;

	public int RefereeCount { get; init; } = 100;

	public int TourneyCount { get; init; } = 400;

	public int ExpectedBeatmapsets { get; init; } = 15;

	/// <summary>Builds the target <see cref="Endpoint"/>.</summary>
	public Endpoint Endpoint => new(Connect, Port, Domain);

	/// <summary>
	/// Computes the bcrypt-safe ramp duration: <c>N * 0.145 / (cores * 0.5)</c>, floored at 15 s. <paramref name="sessions"/>
	/// counts every client that signs in, tourney child clients included, because each sign-in costs about 145 ms of
	/// server CPU and a session signs in once for the life of its copy.
	/// </summary>
	public TimeSpan RampDuration(int sessions)
	{
		var cores = Math.Max(1, BasilCores);
		var seconds = Math.Ceiling(sessions * 0.145 / (cores * 0.5));
		return TimeSpan.FromSeconds(Math.Max(15, seconds));
	}

	/// <summary>Parses command-line arguments (<c>&lt;test&gt; [--flag value ...]</c>).</summary>
	public static LoadTestOptions Parse(string[] args)
	{
		if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
			throw new ArgumentException("help");

		var options = new LoadTestOptions { Test = args[0].ToLowerInvariant() };
		for (var i = 1; i < args.Length; i++)
		{
			var flag = args[i];
			string Next()
			{
				return i + 1 < args.Length
					? args[++i]
					: throw new ArgumentException($"Missing value for {flag}.");
			}

			options = flag switch
			{
				"--connect" => options with { Connect = Next() },
				"--port" => options with { Port = int.Parse(Next()) },
				"--irc-port" => options with { IrcPort = int.Parse(Next()) },
				"--domain" => options with { Domain = Next() },
				"--accounts" => options with { AccountsPath = Next() },
				"--world" => options with { WorldPath = Next() },
				"--reports" => options with { ReportsRoot = Next() },
				"--n" or "--sessions" => options with { TargetSessions = int.Parse(Next()) },
				"--step-delta" => options with { StepDelta = int.Parse(Next()) },
				"--steps" => options with { StepCount = int.Parse(Next()) },
				"--step-minutes" => options with { StepMinutes = int.Parse(Next()) },
				"--cores" => options with { BasilCores = int.Parse(Next()) },
				"--seed" => options with { Seed = int.Parse(Next()) },
				"--duration" => options with { SteadyDuration = ParseDuration(Next()) },
				"--soak-duration" => options with { SoakDuration = ParseDuration(Next()) },
				"--round" => options with { RoundDuration = ParseDuration(Next()) },
				"--poll" => options with { PollInterval = ParseDuration(Next()) },
				"--pilot" => options with { Pilot = true },
				"--owner-user" => options with { OwnerUser = Next() },
				"--owner-pass" => throw new ArgumentException(
					$"--owner-pass is not accepted: it would show in the process list and the shell history. Set {OwnerPasswordVariable} instead."),
				"--players" => options with { PlayerCount = int.Parse(Next()) },
				"--referees" => options with { RefereeCount = int.Parse(Next()) },
				"--tourneys" => options with { TourneyCount = int.Parse(Next()) },
				"--expected-sets" => options with { ExpectedBeatmapsets = int.Parse(Next()) },
				_ => throw new ArgumentException($"Unknown option: {flag}")
			};
		}

		return options;
	}

	private static TimeSpan ParseDuration(string value)
	{
		if (value.EndsWith('h') && double.TryParse(value[..^1], out var hours))
			return TimeSpan.FromHours(hours);
		if (value.EndsWith('m') && double.TryParse(value[..^1], out var mins))
			return TimeSpan.FromMinutes(mins);
		if (value.EndsWith('s') && double.TryParse(value[..^1], out var secs))
			return TimeSpan.FromSeconds(secs);
		return TimeSpan.Parse(value);
	}
}

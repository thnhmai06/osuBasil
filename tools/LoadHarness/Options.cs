using System.Globalization;

namespace LoadHarness;

/// <summary>The command line of one run.</summary>
internal sealed class Options
{
	public const string Usage = """
	                            LoadHarness drives a running Basil server through nginx with osu!, IRC and API clients.

	                              --accounts <file>      CSV of name,password; one account per client (required)
	                              --domain <name>        the server's domain (default basil.local)
	                              --connect <host>       connect here instead of resolving the domain (the domain stays the Host and SNI)
	                              --port <n>             HTTPS port (default 443)
	                              --irc-host <host>      IRC host (default: --connect, else the domain)
	                              --irc-port <n>         IRC over TLS port (default 6697)
	                              --insecure             accept the self-signed certificate
	                              --osu <n> --irc <n> --api <n>   number of clients of each kind (default 0)
	                              --duration <time>      measured time after the ramp (default 60s; ms, s or m)
	                              --ramp <time>          time over which the clients start, not measured (default 10s)
	                              --osu-poll <time>      pause between osu! polls (default 1s)
	                              --irc-interval <time>  pause between IRC messages (default 5s)
	                              --api-interval <time>  pause between API calls (default 1s)
	                              --container <name>     sample this container's CPU and memory with docker stats
	                              --out <file>           JSON result (default load-result.json)
	                            """;

	public string Accounts { get; private set; } = "";
	public string Domain { get; private set; } = "basil.local";
	public string? Connect { get; private set; }
	public int Port { get; private set; } = 443;
	public string? IrcHost { get; private set; }
	public int IrcPort { get; private set; } = 6697;
	public bool Insecure { get; private set; }
	public int Osu { get; private set; }
	public int Irc { get; private set; }
	public int Api { get; private set; }
	public TimeSpan Duration { get; private set; } = TimeSpan.FromSeconds(60);
	public TimeSpan Ramp { get; private set; } = TimeSpan.FromSeconds(10);
	public TimeSpan OsuPoll { get; private set; } = TimeSpan.FromSeconds(1);
	public TimeSpan IrcInterval { get; private set; } = TimeSpan.FromSeconds(5);
	public TimeSpan ApiInterval { get; private set; } = TimeSpan.FromSeconds(1);
	public string? Container { get; private set; }
	public string Out { get; private set; } = "load-result.json";

	public static Options Parse(string[] args)
	{
		var options = new Options();
		for (var i = 0; i < args.Length; i++)
		{
			var name = args[i];
			if (name == "--insecure")
			{
				options.Insecure = true;
				continue;
			}

			if (i + 1 >= args.Length) throw new ArgumentException($"{name} needs a value.");
			var value = args[++i];
			switch (name)
			{
				case "--accounts": options.Accounts = value; break;
				case "--domain": options.Domain = value; break;
				case "--connect": options.Connect = value; break;
				case "--port": options.Port = Count(name, value); break;
				case "--irc-host": options.IrcHost = value; break;
				case "--irc-port": options.IrcPort = Count(name, value); break;
				case "--osu": options.Osu = Count(name, value); break;
				case "--irc": options.Irc = Count(name, value); break;
				case "--api": options.Api = Count(name, value); break;
				case "--duration": options.Duration = Time(name, value); break;
				case "--ramp": options.Ramp = Time(name, value); break;
				case "--osu-poll": options.OsuPoll = Time(name, value); break;
				case "--irc-interval": options.IrcInterval = Time(name, value); break;
				case "--api-interval": options.ApiInterval = Time(name, value); break;
				case "--container": options.Container = value; break;
				case "--out": options.Out = value; break;
				default: throw new ArgumentException($"Unknown option {name}.");
			}
		}

		if (options.Accounts.Length == 0) throw new ArgumentException("--accounts is required.");
		if (options.Osu + options.Irc + options.Api == 0) throw new ArgumentException("Give at least one client.");
		return options;
	}

	private static int Count(string name, string value)
	{
		return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
			? count
			: throw new ArgumentException($"{name} needs a whole number.");
	}

	private static TimeSpan Time(string name, string value)
	{
		var (digits, unit) = value.EndsWith("ms") ? (value[..^2], 1.0) :
			value.EndsWith('s') ? (value[..^1], 1000.0) :
			value.EndsWith('m') ? (value[..^1], 60000.0) : (value, 1000.0);
		return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number >= 0
			? TimeSpan.FromMilliseconds(number * unit)
			: throw new ArgumentException($"{name} needs a time such as 500ms, 60s or 2m.");
	}
}

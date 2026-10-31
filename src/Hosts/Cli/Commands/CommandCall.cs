using System.Globalization;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;

namespace Basil.Host.Cli.Commands;

/// <summary>A refusal or invalid input detected while a command runs; the runner prints it and ends with the matching exit code.</summary>
/// <param name="exitCode">The exit code of the command.</param>
/// <param name="code">The camelCase error code.</param>
/// <param name="message">The human-readable message.</param>
public sealed class CommandException(int exitCode, string code, string message) : Exception(message)
{
	/// <summary>Gets the exit code of the command.</summary>
	public int ExitCode { get; } = exitCode;

	/// <summary>Gets the camelCase error code.</summary>
	public string Code { get; } = code;

	/// <summary>Creates the exception for invalid input.</summary>
	public static CommandException Invalid(string message)
	{
		return new CommandException(ExitCodes.Invalid, "invalid", message);
	}

	/// <summary>Creates the exception for something that does not exist.</summary>
	public static CommandException NotFound(string message)
	{
		return new CommandException(ExitCodes.Refused, "notFound", message);
	}
}

/// <summary>The exit codes of a console command.</summary>
public static class ExitCodes
{
	/// <summary>The command worked.</summary>
	public const int Success = 0;

	/// <summary>The operation was refused: not found, conflict, gone, locked, system account, forbidden.</summary>
	public const int Refused = 1;

	/// <summary>The input or the command line is not valid.</summary>
	public const int Invalid = 2;

	/// <summary>The server is not running and the command needs it.</summary>
	public const int NotRunning = 3;

	/// <summary>The operator declined the confirmation.</summary>
	public const int Declined = 4;
}

/// <summary>Where a command was typed and whether the operator can be asked a question there.</summary>
/// <param name="Dashboard">Whether it was typed in the dashboard.</param>
/// <param name="Confirm">Asks the operator a yes/no question, or <see langword="null" /> when nobody can be asked.</param>
public sealed record CommandSession(bool Dashboard, Func<string, CancellationToken, Task<bool>>? Confirm = null)
{
	/// <summary>A session that cannot ask questions and is not the dashboard: the shell, the socket and tests.</summary>
	public static CommandSession Plain { get; } = new(false);
}

/// <summary>Everything a command needs to run: its words and options, the services of the server, and where to print.</summary>
public sealed class CommandCall
{
	private readonly Dictionary<string, string> _words;
	private readonly Dictionary<string, string> _options;
	private OperatorConnection? _by;

	/// <summary>Creates a call.</summary>
	public CommandCall(IServiceProvider services, ICommandOutput output, ViewFormatter formatter,
		Dictionary<string, string> words, Dictionary<string, string> options, CancellationToken cancellationToken,
		CommandSession? session = null)
	{
		Session = session ?? CommandSession.Plain;
		Services = services;
		Output = output;
		Formatter = formatter;
		_words = words;
		_options = options;
		CancellationToken = cancellationToken;
	}

	/// <summary>Gets where the command was typed.</summary>
	public CommandSession Session { get; }

	/// <summary>Gets the services of the server.</summary>
	public IServiceProvider Services { get; }

	/// <summary>Gets where the command prints.</summary>
	public ICommandOutput Output { get; }

	/// <summary>Gets the formatter of record views.</summary>
	public ViewFormatter Formatter { get; }

	/// <summary>Gets the token that cancels the command.</summary>
	public CancellationToken CancellationToken { get; }

	/// <summary>Gets a service.</summary>
	public T Get<T>() where T : notnull
	{
		return (T)Services.GetService(typeof(T))! ?? throw new InvalidOperationException($"{typeof(T).Name} is not registered.");
	}

	/// <summary>Gets the connection the command acts with: BasilBot, without rights checks.</summary>
	public async Task<OperatorConnection> ByAsync()
	{
		return _by ??= await Get<UserRegistry>().OperateAsync(CancellationToken);
	}

	/// <summary>Reads a numeric word of the command, such as the <c>id</c> of <c>users &lt;id&gt;</c>.</summary>
	public int Id(string name)
	{
		return int.Parse(_words[name], CultureInfo.InvariantCulture);
	}

	/// <summary>Reads a word of the command.</summary>
	public string Word(string name)
	{
		return _words[name];
	}

	/// <summary>Tells whether an option was given.</summary>
	public bool Has(string name)
	{
		return _options.ContainsKey(name);
	}

	/// <summary>Reads a text option.</summary>
	public string? Text(string name)
	{
		return _options.GetValueOrDefault(name);
	}

	/// <summary>Reads a whole-number option.</summary>
	public int? Int(string name)
	{
		return _options.TryGetValue(name, out var text)
			? int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
				? value
				: throw CommandException.Invalid($"--{name} must be a whole number.")
			: null;
	}

	/// <summary>Reads a boolean option; a bare option means <see langword="true" />.</summary>
	public bool? Bool(string name)
	{
		return _options.TryGetValue(name, out var text)
			? text.Length == 0 || text.Equals("true", StringComparison.OrdinalIgnoreCase) ? true
			: text.Equals("false", StringComparison.OrdinalIgnoreCase) ? false
			: throw CommandException.Invalid($"--{name} must be true or false.")
			: null;
	}

	/// <summary>Reads a bitmask option.</summary>
	public ulong? Mask(string name)
	{
		return _options.TryGetValue(name, out var text)
			? ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
				? value
				: throw CommandException.Invalid($"--{name} must be a number (a bitmask).")
			: null;
	}

	/// <summary>Reads a duration option such as <c>30m</c>, <c>12h</c> or <c>7d</c>.</summary>
	public TimeSpan? Duration(string name)
	{
		if (!_options.TryGetValue(name, out var text)) return null;

		if (text.Length > 1 && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) &&
		    amount > 0)
			switch (char.ToLowerInvariant(text[^1]))
			{
				case 's': return TimeSpan.FromSeconds(amount);
				case 'm': return TimeSpan.FromMinutes(amount);
				case 'h': return TimeSpan.FromHours(amount);
				case 'd': return TimeSpan.FromDays(amount);
			}

		throw CommandException.Invalid($"--{name} must be a duration such as 30m, 12h or 7d.");
	}

	/// <summary>Reads a list option written with commas.</summary>
	public string[]? List(string name)
	{
		return _options.TryGetValue(name, out var text)
			? text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			: null;
	}

	/// <summary>Reads an option the command cannot run without.</summary>
	public string Required(string name)
	{
		return _options.TryGetValue(name, out var text) && text.Length > 0
			? text
			: throw CommandException.Invalid($"--{name} is required.");
	}

	/// <summary>Gets every option that was given, for commands that take open-ended fields.</summary>
	public IReadOnlyDictionary<string, string> Options => _options;
}

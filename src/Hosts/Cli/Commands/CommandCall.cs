using System.Globalization;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Registry;

namespace Basil.Host.Cli.Commands;

public sealed class CommandException(int exitCode, string code, string message) : Exception(message)
{
	public int ExitCode { get; } = exitCode;

	public string Code { get; } = code;

	public static CommandException Invalid(string message)
	{
		return new CommandException(ExitCodes.Invalid, "invalid", message);
	}

	public static CommandException NotFound(string message)
	{
		return new CommandException(ExitCodes.Refused, "notFound", message);
	}
}

public static class ExitCodes
{
	public const int Success = 0;

	public const int Refused = 1;

	public const int Invalid = 2;

	public const int NotRunning = 3;

	public const int Declined = 4;
}

public sealed record CommandSession(bool Dashboard, Func<string, CancellationToken, Task<bool>>? Confirm = null)
{
	public static CommandSession Plain { get; } = new(false);
}

public sealed class CommandCall
{
	private readonly Dictionary<string, string> _options;
	private readonly Dictionary<string, string> _words;
	private OperatorConnection? _by;

	public CommandCall(IServiceProvider services, ICommandOutput output, ViewFormatter formatter,
		Dictionary<string, string> words, Dictionary<string, string> options, CancellationToken ct,
		CommandSession? session = null)
	{
		Session = session ?? CommandSession.Plain;
		Services = services;
		Output = output;
		Formatter = formatter;
		_words = words;
		_options = options;
		CancellationToken = ct;
	}

	public CommandSession Session { get; }

	public IServiceProvider Services { get; }

	public ICommandOutput Output { get; }

	public ViewFormatter Formatter { get; }

	public CancellationToken CancellationToken { get; }

	public IReadOnlyDictionary<string, string> Options => _options;

	public T Get<T>() where T : notnull
	{
		return (T)Services.GetService(typeof(T))! ??
		       throw new InvalidOperationException($"{typeof(T).Name} is not registered.");
	}

	public async Task<OperatorConnection> ByAsync()
	{
		return _by ??= await Get<UserRegistry>().OperateAsync(CancellationToken);
	}

	public int Id(string name)
	{
		return int.Parse(_words[name], CultureInfo.InvariantCulture);
	}

	public string Word(string name)
	{
		return _words[name];
	}

	public bool Has(string name)
	{
		return _options.ContainsKey(name);
	}

	public string? Text(string name)
	{
		return _options.GetValueOrDefault(name);
	}

	public int? Int(string name)
	{
		return _options.TryGetValue(name, out var text)
			? int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
				? value
				: throw CommandException.Invalid($"--{name} must be a whole number.")
			: null;
	}

	public bool? Bool(string name)
	{
		return _options.TryGetValue(name, out var text)
			? text.Length == 0 || text.Equals("true", StringComparison.OrdinalIgnoreCase) ? true
			: text.Equals("false", StringComparison.OrdinalIgnoreCase) ? false
			: throw CommandException.Invalid($"--{name} must be true or false.")
			: null;
	}

	public ulong? Mask(string name)
	{
		return _options.TryGetValue(name, out var text)
			? ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
				? value
				: throw CommandException.Invalid($"--{name} must be a number (a bitmask).")
			: null;
	}

	public TimeSpan? Duration(string name)
	{
		if (!_options.TryGetValue(name, out var text)) return null;

		if (text.Length > 1 &&
		    double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) &&
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

	public string[]? List(string name)
	{
		return _options.TryGetValue(name, out var text)
			? text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			: null;
	}

	public string Required(string name)
	{
		return _options.TryGetValue(name, out var text) && text.Length > 0
			? text
			: throw CommandException.Invalid($"--{name} is required.");
	}
}
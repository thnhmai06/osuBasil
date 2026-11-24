using System.Collections.Frozen;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Basil.Application.Bot.Replies;
using Basil.Infrastructure.Shared.Storage;
using Jeffijoe.MessageFormat;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Bot.Services;

internal sealed class ReplyCatalog(
	IEnumerable<Reply> replies,
	DataPaths paths,
	ILogger<ReplyCatalog> logger) : IReplies
{
	// The editor's link to the schema; it is no reply.
	private const string SchemaKey = "$schema";

	private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en");

	private static readonly JsonSerializerOptions FileFormat = new()
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	private readonly MessageFormatter _formatter = new();
	private readonly ILogger<ReplyCatalog> _logger = logger;

	private readonly FrozenDictionary<string, Reply> _replies =
		replies.ToFrozenDictionary(reply => reply.Key, StringComparer.Ordinal);

	private FrozenDictionary<string, string> _texts = FrozenDictionary<string, string>.Empty;

	public string Path { get; } =
		System.IO.Path.GetFullPath(System.IO.Path.Combine(paths.Root, "Localizations", "bot.json"));

	public string Text(Reply reply, object? arguments = null)
	{
		var values = ArgumentsOf(arguments);
		foreach (var name in reply.Arguments)
			if (!values.ContainsKey(name))
				throw new ArgumentException($"The reply {reply.Key} needs the argument {name}.", nameof(arguments));

		if (Volatile.Read(ref _texts).TryGetValue(reply.Key, out var text))
			try
			{
				return Format(text, values);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_logger.LogWarning(exception,
					"The wording of the reply {Key} cannot be filled in; the default wording is used", reply.Key);
			}

		return Format(reply.Default, values);
	}

	public void Load()
	{
		if (!File.Exists(Path))
			WriteDefaults();

		Reload();
	}

	public bool Reload()
	{
		Dictionary<string, JsonElement> entries;
		try
		{
			using var stream = File.OpenRead(Path);
			entries = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(stream) ??
			          throw new JsonException("The file holds no object.");
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
		{
			_logger.LogWarning(exception, "The replies file {Path} cannot be read; the wording in use stays", Path);
			return false;
		}

		var texts = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var (key, value) in entries)
		{
			if (key == SchemaKey) continue;

			if (!_replies.TryGetValue(key, out var reply))
			{
				_logger.LogWarning("The replies file {Path} has the text {Key}, which is no reply of BasilBot", Path,
					key);
				continue;
			}

			if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text)
			{
				_logger.LogWarning("The text of the reply {Key} is not a string; the default wording is used", key);
				continue;
			}

			if (!IsValid(reply, text, out var problem))
			{
				_logger.LogWarning("The text of the reply {Key} is not valid ({Problem}); the default wording is used",
					key, problem);
				continue;
			}

			texts[key] = text;
		}

		Volatile.Write(ref _texts, texts.ToFrozenDictionary(StringComparer.Ordinal));
		return true;
	}

	private void WriteDefaults()
	{
		try
		{
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
			var defaults = _replies.Values.OrderBy(reply => reply.Key, StringComparer.Ordinal)
				.ToDictionary(reply => reply.Key, reply => reply.Default);
			var temporary = Path + ".tmp";
			File.WriteAllText(temporary, JsonSerializer.Serialize(defaults, FileFormat));
			File.Move(temporary, Path, true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			_logger.LogWarning(exception, "The replies file {Path} cannot be written; the default wording is used",
				Path);
		}
	}

	private bool IsValid(Reply reply, string text, out string? problem)
	{
		// A text may use only the arguments its reply declares, so it is filled in with exactly those.
		var sample = reply.Arguments.ToDictionary(name => name, _ => (object?)1, StringComparer.Ordinal);
		try
		{
			Format(text, sample);
			problem = null;
			return true;
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			problem = exception.Message;
			return false;
		}
	}

	private string Format(string text, IReadOnlyDictionary<string, object?> values)
	{
		lock (_formatter)
		{
			return _formatter.FormatMessage(text, values, Culture);
		}
	}

	private static IReadOnlyDictionary<string, object?> ArgumentsOf(object? arguments)
	{
		return arguments switch
		{
			null => FrozenDictionary<string, object?>.Empty,
			IReadOnlyDictionary<string, object?> values => values,
			_ => arguments.GetType().GetProperties().ToDictionary(property => property.Name,
				property => property.GetValue(arguments), StringComparer.Ordinal)
		};
	}
}
using System.Collections.Frozen;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Basil.Application.Shared.Replies;
using Basil.Infrastructure.Shared.Storage;
using Jeffijoe.MessageFormat;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Shared.Replies;

/// <summary>Words the replies of one surface from <c>Data/Localizations/&lt;surface&gt;.json</c>, falling back to the default wording a reply declares.</summary>
/// <typeparam name="TReply">The reply enum of the surface.</typeparam>
public sealed class ReplyCatalog<TReply>(string surface, DataPaths paths, ILogger<ReplyCatalog<TReply>> logger)
	: IReplies<TReply> where TReply : struct, Enum
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
	private readonly ILogger<ReplyCatalog<TReply>> _logger = logger;

	private readonly FrozenDictionary<string, ReplyDefinition> _replies = Enum.GetValues<TReply>()
		.Select(reply => new ReplyDefinition(reply))
		.ToFrozenDictionary(definition => definition.Key, StringComparer.Ordinal);

	private FrozenDictionary<string, string> _texts = FrozenDictionary<string, string>.Empty;

	internal string Path { get; } =
		System.IO.Path.GetFullPath(System.IO.Path.Combine(paths.Root, "Localizations", surface + ".json"));

	public string Text(TReply reply, object? arguments = null)
	{
		var definition = _replies[reply.ToString()];
		var values = ArgumentsOf(arguments);
		foreach (var name in definition.Arguments)
			if (!values.ContainsKey(name))
				throw new ArgumentException($"The reply {definition.Key} needs the argument {name}.", nameof(arguments));

		if (Volatile.Read(ref _texts).TryGetValue(definition.Key, out var text))
			try
			{
				return Format(text, values);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_logger.LogWarning(exception,
					"The wording of the reply {Key} cannot be filled in; the default wording is used", definition.Key);
			}

		return Format(definition.Default, values);
	}

	internal void Load()
	{
		if (!File.Exists(Path))
			WriteDefaults();

		Reload();
	}

	internal bool Reload()
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
				_logger.LogWarning("The replies file {Path} has the text {Key}, which is no reply of this surface", Path,
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

	private bool IsValid(ReplyDefinition reply, string text, out string? problem)
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

internal sealed class ReplyDefinition
{
	public ReplyDefinition(Enum reply)
	{
		Key = reply.ToString();
		var attribute = ReplyAttribute.Of(reply);
		Default = attribute.Text;
		Arguments = attribute.Arguments;
	}

	public string Key { get; }

	public string Default { get; }

	public string[] Arguments { get; }
}

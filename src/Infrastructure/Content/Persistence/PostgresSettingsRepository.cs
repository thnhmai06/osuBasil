using System.Text.Json;
using System.Text.Json.Serialization;
using Basil.Application.Content.Persistence;
using Basil.Domain.Content;
using Basil.Infrastructure.Common;
using Basil.Infrastructure.Common.Registry;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Content.Persistence;

/// <summary>Stores the server-wide settings.</summary>
internal sealed class PostgresSettingsRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	ILogger<PostgresSettingsRepository> logger)
	: ISettingsRepository, IResident
{
	private readonly ResidentWrites<int> _writes = new();
	private ServerSettings? _settings;

	/// <inheritdoc />
	public async Task LoadAsync(CancellationToken cancellationToken)
	{
		var rows = await reader.ReadAsync((ContentDbContext context) => context.Settings
			.ToListAsync(cancellationToken), cancellationToken);
		var values = rows.ToDictionary(row => row.Name, row => row.Value, StringComparer.Ordinal);

		// A category without a row, or whose row cannot be read, keeps its defaults.
		var settings = new ServerSettings();
		foreach (var category in Categories)
			if (values.GetValueOrDefault(category.Name) is { } json)
				try
				{
					settings = category.Read(settings, json);
				}
				catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException)
				{
					logger.LogWarning(exception, "The setting category {Category} is not valid and keeps its defaults",
						category.Name);
				}

		_settings = settings;
	}

	/// <inheritdoc />
	public ServerSettings Current =>
		Volatile.Read(ref _settings) ?? throw new InvalidOperationException(
			"Server settings are read before the storage has started.");

	/// <inheritdoc />
	public ValueTask<ServerSettings> GetAsync(CancellationToken cancellationToken = default)
	{
		return ValueTask.FromResult(Volatile.Read(ref _settings)
		                            ?? throw new InvalidOperationException(
			                            "Server settings are read before the storage has started."));
	}

	/// <inheritdoc />
	public async Task CreateOrUpdateAsync(ServerSettings settings, CancellationToken cancellationToken = default)
	{
		var values = Categories.ToDictionary(category => category.Name, category => category.Write(settings),
			StringComparer.Ordinal);
		await _writes.WriteAsync(1, () => writer.SaveAsync(Root.Server, (GetType(), 1),
			new WriteCommand<ContentDbContext>(async (context, token) =>
			{
				foreach (var (name, value) in values)
					await UpsertSettingAsync(context, name, value, token);
			})), () => Interlocked.Exchange(ref _settings, settings));
	}

	private static Task UpsertSettingAsync(ContentDbContext context, string name, string? value,
		CancellationToken token)
	{
		return context.UpsertAsync(new SettingRow { Name = name, Value = value }, stored => stored.Name == name, token);
	}

	/// <summary>One nested category of the settings, stored as one JSON row.</summary>
	private sealed record Category(string Name, Func<ServerSettings, string> Write,
		Func<ServerSettings, string, ServerSettings> Read);

	private static Category Of<T>(string name, Func<ServerSettings, T> get, Func<ServerSettings, T, ServerSettings> set)
		where T : notnull
	{
		return new Category(name, settings => JsonSerializer.Serialize(get(settings), Json),
			(settings, json) => set(settings, JsonSerializer.Deserialize<T>(json, Json) ??
			                                  throw new JsonException("The category is null.")));
	}

	private static readonly JsonSerializerOptions Json = new()
	{
		Converters = { new JsonStringEnumConverter(), new ContentReferenceConverter() }
	};

	private static readonly Category[] Categories =
	[
		Of("general", settings => settings.General, (settings, value) => settings with { General = value }),
		Of("locks", settings => settings.Locks, (settings, value) => settings with { Locks = value }),
		Of("sessions", settings => settings.Sessions, (settings, value) => settings with { Sessions = value }),
		Of("rate_limits", settings => settings.RateLimits, (settings, value) => settings with { RateLimits = value }),
		Of("uploads", settings => settings.Uploads, (settings, value) => settings with { Uploads = value }),
		Of("beatmaps", settings => settings.Beatmaps, (settings, value) => settings with { Beatmaps = value }),
		Of("multiplayer", settings => settings.Multiplayer, (settings, value) => settings with { Multiplayer = value }),
		Of("bot", settings => settings.Bot, (settings, value) => settings with { Bot = value }),
		Of("cors", settings => settings.Cors, (settings, value) => settings with { Cors = value }),
		Of("menu_icon", settings => settings.MenuIcon, (settings, value) => settings with { MenuIcon = value })
	];

	private sealed class ContentReferenceConverter : JsonConverter<ContentReference>
	{
		public override ContentReference Read(ref Utf8JsonReader reader, Type typeToConvert,
			JsonSerializerOptions options)
		{
			return ContentReference.Parse(reader.GetString() ?? throw new JsonException(), null);
		}

		public override void Write(Utf8JsonWriter writer, ContentReference value, JsonSerializerOptions options)
		{
			writer.WriteStringValue(value.ToString());
		}
	}
}

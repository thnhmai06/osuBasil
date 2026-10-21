using System.Globalization;
using Basil.Application.Persistence.Content;
using Basil.Domain;
using Basil.Domain.Content;
using Basil.Infrastructure.Persistence.Common;
using Basil.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Persistence.Content;

/// <summary>Stores the server-wide settings.</summary>
internal sealed class PostgresSettingsRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	ILogger<PostgresSettingsRepository> logger)
	: ISettingsRepository, IResident
{
	private ServerSettings? _settings;

	/// <inheritdoc />
	public async Task LoadAsync(CancellationToken cancellationToken)
	{
		var rows = await reader.ReadAsync(context => context.Settings
			.ToListAsync(cancellationToken), cancellationToken);
		var values = rows.ToDictionary(row => row.Name, row => row.Value, StringComparer.Ordinal);

		// A setting without a row keeps its default.
		var defaults = new ServerSettings();
		_settings = new ServerSettings
		{
			Motd = values.GetValueOrDefault("motd"),
			LockedCreation = ParseLocks(values.GetValueOrDefault("locked_creation")) ?? defaults.LockedCreation,
			MenuIcon = new ServerSettings.MenuIconSettings
			{
				Url = Parse("menu_icon_url", values.GetValueOrDefault("menu_icon_url")),
				Image = ParseImage(values.GetValueOrDefault("menu_icon_image"))
			},
			BeatmapMirror = new ServerSettings.BeatmapMirrorSettings
			{
				DownloadEndpoint = Parse("mirror_download_endpoint", values.GetValueOrDefault("mirror_download_endpoint")),
				SearchEndpoint = Parse("mirror_search_endpoint", values.GetValueOrDefault("mirror_search_endpoint"))
			}
		};
	}

	/// <inheritdoc />
	public ValueTask<ServerSettings> GetAsync(CancellationToken cancellationToken = default)
	{
		return ValueTask.FromResult(Volatile.Read(ref _settings)
		                            ?? throw new InvalidOperationException(
			                            "Server settings are read before the storage has started."));
	}

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(ServerSettings settings, CancellationToken cancellationToken = default)
	{
		var values = new Dictionary<string, string?>
		{
			["motd"] = settings.Motd,
			["locked_creation"] = ((int)settings.LockedCreation).ToString(CultureInfo.InvariantCulture),
			["menu_icon_url"] = settings.MenuIcon.Url?.ToString(),
			["menu_icon_image"] = settings.MenuIcon.Image?.ToString(),
			["mirror_download_endpoint"] = settings.BeatmapMirror.DownloadEndpoint?.ToString(),
			["mirror_search_endpoint"] = settings.BeatmapMirror.SearchEndpoint?.ToString()
		};
		Interlocked.Exchange(ref _settings, settings);
		_ = writer.SaveAsync(Root.Server, (GetType(), 1), new WriteCommand(
			async (context, token) =>
			{
				foreach (var (name, value) in values)
					await UpsertSettingAsync(context, name, value, token);
			}));
		return Task.CompletedTask;
	}

	private static Task UpsertSettingAsync(BasilDbContext context, string name, string? value, CancellationToken token)
	{
		return context.UpsertAsync(new SettingRow { Name = name, Value = value }, stored => stored.Name == name, token);
	}

	/// <summary>Reads an absolute address, or <see langword="null" /> when none or an invalid one is stored.</summary>
	private Uri? Parse(string name, string? value)
	{
		if (value is null) return null;
		if (Uri.TryCreate(value, UriKind.Absolute, out var address)) return address;

		logger.LogWarning("The setting {Setting} holds {Value}, which is not an absolute address, and is not used", name, value);
		return null;
	}

	/// <summary>Reads the locked creations, or <see langword="null" /> when none or an invalid value is stored.</summary>
	private ServerSettings.CreationLocks? ParseLocks(string? value)
	{
		if (value is null) return null;
		if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var locks))
			return (ServerSettings.CreationLocks)locks;

		logger.LogWarning("The setting locked_creation holds {Value}, which is not a number, and is not used", value);
		return null;
	}

	/// <summary>Reads the stored menu icon image, or <see langword="null" /> when none or an invalid one is stored.</summary>
	private ContentReference? ParseImage(string? value)
	{
		if (value is null) return null;
		if (ContentReference.TryParse(value, null, out var image)) return image;

		logger.LogWarning("The menu icon image {Image} is not a valid asset reference and is not shown", value);
		return null;
	}
}
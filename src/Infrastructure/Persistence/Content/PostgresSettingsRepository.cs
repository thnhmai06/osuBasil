using System.Globalization;
using Basil.Application.Persistence.Content;
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
	private readonly ResidentWrites<int> _writes = new();
	private ServerSettings? _settings;

	/// <inheritdoc />
	public async Task LoadAsync(CancellationToken cancellationToken)
	{
		var rows = await reader.ReadAsync((ContentDbContext context) => context.Settings
			.ToListAsync(cancellationToken), cancellationToken);
		var values = rows.ToDictionary(row => row.Name, row => row.Value, StringComparer.Ordinal);

		// A setting without a row keeps its default.
		var defaults = new ServerSettings();
		_settings = new ServerSettings
		{
			Motd = values.GetValueOrDefault(SettingNames.Motd),
			LockedCreation = ParseLocks(values.GetValueOrDefault(SettingNames.LockedCreation)) ??
			                 defaults.LockedCreation,
			MenuIcon = new ServerSettings.MenuIconSettings
			{
				Url = Parse(SettingNames.MenuIconUrl, values.GetValueOrDefault(SettingNames.MenuIconUrl)),
				Image = ParseImage(values.GetValueOrDefault(SettingNames.MenuIconImage))
			},
			BeatmapMirror = new ServerSettings.BeatmapMirrorSettings
			{
				DownloadEndpoint = Parse(SettingNames.MirrorDownloadEndpoint,
					values.GetValueOrDefault(SettingNames.MirrorDownloadEndpoint)),
				SearchEndpoint = Parse(SettingNames.MirrorSearchEndpoint,
					values.GetValueOrDefault(SettingNames.MirrorSearchEndpoint))
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
	public async Task CreateOrUpdateAsync(ServerSettings settings, CancellationToken cancellationToken = default)
	{
		var values = new Dictionary<string, string?>
		{
			[SettingNames.Motd] = settings.Motd,
			[SettingNames.LockedCreation] = ((int)settings.LockedCreation).ToString(CultureInfo.InvariantCulture),
			[SettingNames.MenuIconUrl] = settings.MenuIcon.Url?.ToString(),
			[SettingNames.MenuIconImage] = settings.MenuIcon.Image?.ToString(),
			[SettingNames.MirrorDownloadEndpoint] = settings.BeatmapMirror.DownloadEndpoint?.ToString(),
			[SettingNames.MirrorSearchEndpoint] = settings.BeatmapMirror.SearchEndpoint?.ToString()
		};
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

	/// <summary>Reads an absolute address, or <see langword="null" /> when none or an invalid one is stored.</summary>
	private Uri? Parse(string name, string? value)
	{
		if (value is null) return null;
		if (Uri.TryCreate(value, UriKind.Absolute, out var address)) return address;

		logger.LogWarning("The setting {Setting} holds {Value}, which is not an absolute address, and is not used",
			name, value);
		return null;
	}

	/// <summary>Reads the locked creations, or <see langword="null" /> when none or an invalid value is stored.</summary>
	private ServerSettings.CreationLocks? ParseLocks(string? value)
	{
		if (value is null) return null;
		if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var locks))
			return (ServerSettings.CreationLocks)locks;

		logger.LogWarning("The setting {Setting} holds {Value}, which is not a number, and is not used",
			SettingNames.LockedCreation, value);
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

	/// <summary>The names the settings are stored under.</summary>
	private static class SettingNames
	{
		public const string Motd = "motd";
		public const string LockedCreation = "locked_creation";
		public const string MenuIconUrl = "menu_icon_url";
		public const string MenuIconImage = "menu_icon_image";
		public const string MirrorDownloadEndpoint = "mirror_download_endpoint";
		public const string MirrorSearchEndpoint = "mirror_search_endpoint";
	}
}
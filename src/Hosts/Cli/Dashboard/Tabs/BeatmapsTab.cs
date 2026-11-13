using System.Globalization;
using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Registry;
using Basil.Domain.Beatmaps;
using Basil.Host.Cli.Dashboard.Shell;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal.UI;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>One difficulty in the beatmaps table, with the facts of its beatmapset.</summary>
/// <param name="Id">The beatmap id.</param>
/// <param name="SetId">The id of the beatmapset it belongs to.</param>
/// <param name="Version">The difficulty name.</param>
/// <param name="Status">The ranked status of the beatmapset.</param>
/// <param name="Local">Whether the beatmap has no osu! id and was given a local one.</param>
/// <param name="Modified">When the difficulty was last imported.</param>
/// <param name="Uploaded">When the difficulty was first imported.</param>
/// <param name="Cs">The circle size.</param>
/// <param name="Ar">The approach rate.</param>
/// <param name="Od">The overall difficulty.</param>
/// <param name="Hp">The health drain.</param>
/// <param name="Stars">The star rating, shown for display only.</param>
/// <param name="Length">The length of the beatmap.</param>
/// <param name="Private">Whether the beatmapset is hidden.</param>
/// <param name="Locked">Whether the beatmapset is write-locked.</param>
/// <param name="Versions">How many difficulties the beatmapset has.</param>
internal sealed record BeatmapItem(int Id, int SetId, string Version, BeatmapsetStatus Status, bool Local,
	DateTimeOffset Modified, DateTimeOffset Uploaded, double Cs, double Ar, double Od, double Hp, double Stars,
	TimeSpan Length, bool Private, bool Locked, int Versions);

/// <summary>Reads the beatmapsets a search finds, with their difficulties.</summary>
internal sealed class BeatmapsSource(IServiceProvider services) : IListSource<BeatmapItem>
{
	/// <inheritdoc />
	public async Task<IReadOnlyList<SectionData<BeatmapItem>>> LoadAsync(string query, int limit,
		CancellationToken ct)
	{
		var actor = await services.GetRequiredService<UserRegistry>().OperateAsync(ct);
		var sender = services.GetRequiredService<ISender>();
		var found = await sender.Send(new SearchBeatmapsets(actor, BeatmapQuery.Parse(query),
			new PageRequest(0, limit)), ct);
		var sections = new List<SectionData<BeatmapItem>>();
		foreach (var set in found.Items)
		{
			if (await sender.Send(new GetBeatmapset(actor, set.Id), ct) is not { } detail) continue;

			var title = $"{set.Id} {set.Metadata.Artist} - {set.Metadata.Title}";
			sections.Add(new SectionData<BeatmapItem>(title,
				[.. detail.Beatmaps.Select(map => Item(map, set, detail.Beatmaps.Count))], false));
		}

		// The last section stands for the sets not loaded yet.
		return found.HasNext && sections.Count > 0
			? [.. sections[..^1], sections[^1] with { HasMore = true }]
			: sections;
	}

	private static BeatmapItem Item(BeatmapRecord map, BeatmapsetRecord set, int versions)
	{
		var d = map.Metadata.Difficulty;
		return new BeatmapItem(map.Id, set.Id, map.Metadata.Version, set.Status, map.IsLocallyIngested,
			map.UpdatedAt, map.CreatedAt, d.Cs, d.Ar, d.Od, d.Hp, d.Star, d.Length, !set.Visible, set.Locked,
			versions);
	}
}

/// <summary>The beatmaps: each beatmapset with its difficulties, and what can be done to the set.</summary>
internal sealed class BeatmapsTab : ITabView
{
	private readonly ListPage<BeatmapItem> _page;
	private bool _opened;

	/// <summary>Creates the tab.</summary>
	public BeatmapsTab(IServiceProvider services, ConsoleTab console, CliOptions options, Glyphs glyphs,
		TimeProvider clock, Action<Action> post, ILogger logger)
	{
		_page = new ListPage<BeatmapItem>("Beatmaps",
		[
			new Column<BeatmapItem>("ID", 10, b => b.Id.ToString()),
			new Column<BeatmapItem>("Version", 0, b => b.Version),
			new Column<BeatmapItem>("Status", 10, b => b.Status.ToString(), b => Status(b.Status)),
			new Column<BeatmapItem>("Local", 6, b => b.Local ? glyphs.Yes : glyphs.No, _ => Ink.Muted),
			new Column<BeatmapItem>("Modified", 12, b => Date(b.Modified), _ => Ink.Muted),
			new Column<BeatmapItem>("Uploaded", 12, b => Date(b.Uploaded), _ => Ink.Muted),
			new Column<BeatmapItem>("CS", 5, b => Number(b.Cs)),
			new Column<BeatmapItem>("AR", 5, b => Number(b.Ar)),
			new Column<BeatmapItem>("OD", 5, b => Number(b.Od)),
			new Column<BeatmapItem>("HP", 5, b => Number(b.Hp)),
			new Column<BeatmapItem>("SR", 6, b => Number(b.Stars), _ => Ink.Accent)
		], b => b.Id.ToString(), new BeatmapsSource(services), b => Detail(b, console, options), glyphs,
			options.NoColor, clock, post, logger);
	}

	/// <inheritdoc />
	public Visual View => _page.View;

	/// <inheritdoc />
	public void Attach(TerminalApp app)
	{
		_page.Attach(app);
	}

	/// <inheritdoc />
	public void Activate()
	{
		if (_opened) return;

		_opened = true;
		_page.Refresh();
	}

	/// <inheritdoc />
	public void Tick()
	{
		_page.Tick();
	}

	/// <inheritdoc />
	public void Refresh()
	{
		_page.Refresh();
	}

	/// <summary>Selects a beatmap by its id when it is in the list.</summary>
	/// <param name="id">The beatmap id.</param>
	public void Select(string id)
	{
		_page.Select(id);
	}

	private static string Date(DateTimeOffset time)
	{
		return time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
	}

	private static string Number(double value)
	{
		return value.ToString("0.##", CultureInfo.InvariantCulture);
	}

	private static Ink Status(BeatmapsetStatus status)
	{
		return status is BeatmapsetStatus.Ranked or BeatmapsetStatus.Approved ? Ink.Success
			: status is BeatmapsetStatus.Loved ? Ink.Accent
			: status is BeatmapsetStatus.Qualified ? Ink.Info
			: Ink.Muted;
	}

	private static DetailView Detail(BeatmapItem map, ConsoleTab console, CliOptions options)
	{
		var fields = new List<(string, string, Ink)>
		{
			("Beatmap", $"{map.Id} {map.Version}", Ink.Text),
			("CS/AR/OD/HP", $"{Number(map.Cs)} / {Number(map.Ar)} / {Number(map.Od)} / {Number(map.Hp)}", Ink.Text),
			("SR", Number(map.Stars), Ink.Accent), ("Length", map.Length.ToString(@"m\:ss"), Ink.Text),
			("Modified", Date(map.Modified), Ink.Text), ("Beatmapset", map.SetId.ToString(), Ink.Text),
			("Status", map.Status.ToString(), Status(map.Status)),
			("Private", map.Private ? "yes" : "no", map.Private ? Ink.Warning : Ink.Text),
			("Locked", map.Locked ? "yes" : "no", map.Locked ? Ink.Warning : Ink.Text),
			("Versions", map.Versions.ToString(), Ink.Text)
		};
		if (!options.Editable) return new DetailView(fields, []);

		var set = map.SetId;
		return new DetailView(fields,
		[
			new DetailAction(map.Private ? "Set public" : "Set private",
				() => Run(console, $"beatmapsets {set} set --visible {(map.Private ? "true" : "false")}")),
			new DetailAction(map.Locked ? "Unlock" : "Set locked",
				() => Run(console, $"beatmapsets {set} set --locked {(map.Locked ? "false" : "true")}"))
		]);
	}

	private static Task Run(ConsoleTab console, string line)
	{
		console.RunCommand(line);
		return Task.CompletedTask;
	}
}

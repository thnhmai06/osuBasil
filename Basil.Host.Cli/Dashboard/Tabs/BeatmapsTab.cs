using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Sessions.Registry;
using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;
using Basil.Host.Cli.Dashboard.Shell;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal.UI;
using static Basil.Host.Cli.Dashboard.Tabs.BeatmapDetails;

namespace Basil.Host.Cli.Dashboard.Tabs;

internal sealed record BeatmapItem(
	int Id,
	int SetId,
	string Version,
	BeatmapsetStatus Status,
	bool Local,
	DateTimeOffset Modified,
	DateTimeOffset Uploaded,
	double Cs,
	double Ar,
	double Od,
	double Hp,
	double Stars,
	TimeSpan Length,
	bool Private,
	bool Locked,
	int Versions,
	string Name = "");

internal sealed class BeatmapsSource(IServiceProvider services) : IListSource<BeatmapItem>
{
	public async Task<IReadOnlyList<SectionData<BeatmapItem>>> LoadAsync(string query, int limit,
		CancellationToken ct)
	{
		var actor = await services.GetRequiredService<BotSessions>().OperateAsync(ct);
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

	internal static BeatmapItem Item(BeatmapRecord map, BeatmapsetRecord set, int versions)
	{
		var d = map.Metadata.Difficulty;
		return new BeatmapItem(map.Id, set.Id, map.Metadata.VersionName, set.Status, map.IsLocallyIngested,
			map.UpdatedAt, map.CreatedAt, d?.Cs ?? 0, d?.Ar ?? 0, d?.Od ?? 0, d?.Hp ?? 0, d?.Star ?? 0, d?.Length ?? TimeSpan.Zero,
			!set.Visible, set.Locked,
			versions, $"{set.Metadata.Artist} - {set.Metadata.Title} [{map.Metadata.VersionName}]");
	}
}

internal sealed class BeatmapsTab : ITabView, ISelectable, ISearchable
{
	private const string Help = """
	                            Words match the artist, title, creator and difficulty; "quoted" keeps spaces.
	                            stars ar cs od hp bpm length circles sliders: use = < <= > >=  (stars>5 ar=9)
	                            mode=0|1|2|3     game mode        hidden=1|0|*   hidden state
	                            status=ranked|approved|qualified|loved|pending
	                            creator= artist= title= difficulty= filename=  text the field contains
	                            created= updated=  date comparison
	                            Example: stars>5 ar=9 mode=0 "some title"
	                            """;

	private readonly ListPage<BeatmapItem> _page;
	private bool _opened;

	public BeatmapsTab(IServiceProvider services, BeatmapDetails details, CliOptions options, Glyphs glyphs,
		ShellKit kit, TimeProvider clock, Action<Action> post, ILogger logger)
	{
		_page = new ListPage<BeatmapItem>("Beatmaps", Help,
			[
				new Column<BeatmapItem>("ID", 10, b => b.Id.ToString()) { Right = true },
				new Column<BeatmapItem>("Version", 0, b => b.Version),
				new Column<BeatmapItem>("Status", 10, b => b.Status.ToString(), b => Status(b.Status)),
				new Column<BeatmapItem>("Local", 6, b => b.Local ? glyphs.Yes : glyphs.No, _ => Ink.Muted),
				new Column<BeatmapItem>("Modified", 12, b => Date(b.Modified), _ => Ink.Muted),
				new Column<BeatmapItem>("Uploaded", 12, b => Date(b.Uploaded), _ => Ink.Muted),
				new Column<BeatmapItem>("CS", 5, b => Number(b.Cs)) { Right = true },
				new Column<BeatmapItem>("AR", 5, b => Number(b.Ar)) { Right = true },
				new Column<BeatmapItem>("OD", 5, b => Number(b.Od)) { Right = true },
				new Column<BeatmapItem>("HP", 5, b => Number(b.Hp)) { Right = true },
				new Column<BeatmapItem>("SR", 6, b => Number(b.Stars), _ => Ink.Accent) { Right = true }
			], b => b.Id.ToString(), new BeatmapsSource(services), details.Of, glyphs,
			kit, options.NoColor, clock, post, logger,
			empty: "There are no beatmapsets yet. Add them by copying .osz files into the data directory, with the beatmapsets create command, or with POST /v1/beatmapsets.");
	}

	public void FocusSearch()
	{
		_page.FocusSearch();
	}

	public void ShowHelp()
	{
		_page.ShowHelp();
	}

	public void Select(string id)
	{
		_page.Select(id);
	}

	public Visual View => _page.View;

	public void Attach(TerminalApp app)
	{
		_page.Attach(app);
	}

	public void Activate()
	{
		_page.FocusList();
		if (_opened) return;

		_opened = true;
		_page.Refresh();
	}

	public void Tick()
	{
		_page.Tick();
	}

	public void Refresh()
	{
		_page.Refresh();
	}
}
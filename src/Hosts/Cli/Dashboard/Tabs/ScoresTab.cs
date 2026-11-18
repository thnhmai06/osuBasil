using System.Globalization;
using System.Text.RegularExpressions;
using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Scores.Messages;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Shell;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal.UI;

namespace Basil.Host.Cli.Dashboard.Tabs;

internal sealed record ScoreItem(int Id, int? UserId, string Player, ulong Mask, int? BeatmapId, string Beatmap, string Mods,
	int Score, double Accuracy, Grade Grade, DateTimeOffset Time);

internal sealed partial class ScoresSource(IServiceProvider services) : IListSource<ScoreItem>
{
	public async Task<IReadOnlyList<SectionData<ScoreItem>>> LoadAsync(string query, int limit,
		CancellationToken ct)
	{
		var (user, beatmapId, beatmaps) = Parse(query);
		var sender = services.GetRequiredService<ISender>();
		var listing = await sender.Send(new SearchScores(user, beatmapId, beatmaps, new PageRequest(0, limit)), ct);
		if (listing.Scores is not { } page) return [new SectionData<ScoreItem>("Scores", [], false)];

		var players = new Dictionary<int, (string Name, ulong Mask)>();
		var maps = new Dictionary<string, (int? Id, string Name)>();
		var items = new List<ScoreItem>();
		foreach (var score in page.Items)
		{
			var data = score.Value;
			var player = ("-", 0UL);
			if (data.UserId is { } id && !players.TryGetValue(id, out player))
			{
				var found = await sender.Send(new GetUser(UserReference.ById(id)), ct);
				players[id] = player = (found?.Value.Name ?? id.ToString(CultureInfo.InvariantCulture),
					found is null ? 0UL : (ulong)found.Value.Privileges);
			}

			var map = (Id: (int?)null, Name: "-");
			if (data.BeatmapHash is { } hash && !maps.TryGetValue(hash.HashValue, out map))
			{
				var found = await sender.Send(new FindBeatmapByHash(hash), ct);
				maps[hash.HashValue] = map = found is null ? (null, hash.HashValue) : (found.Id, found.Name);
			}

			items.Add(new ScoreItem(score.Id, data.UserId, player.Item1, player.Item2, map.Id, map.Name, data.Mods.ToString(),
				data.LastState.TotalScore, data.Accuracy, data.Grade, data.Timestamp));
		}

		return [new SectionData<ScoreItem>("Scores", items, page.HasNext)];
	}

	private static (UserReference? User, int? BeatmapId, BeatmapQuery? Beatmaps) Parse(string text)
	{
		UserReference? user = null;
		int? beatmapId = null;
		BeatmapQuery? beatmaps = null;
		foreach (Match match in Token().Matches(text))
		{
			var value = match.Groups[2].Value.Trim('"');
			if (value.Length == 0) continue;

			if (match.Groups[1].Value.Equals("user", StringComparison.OrdinalIgnoreCase))
				user = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
					? UserReference.ById(id)
					: UserReference.ByName(value);
			else if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var map))
				beatmapId = map;
			else
				beatmaps = BeatmapQuery.Parse(value);
		}

		return (user, beatmapId, beatmaps);
	}

	[GeneratedRegex("""\b(user|beatmap):("[^"]*"|\S+)""", RegexOptions.IgnoreCase)]
	private static partial Regex Token();
}

internal sealed class ScoresTab : ITabView, ISearchable
{
	private const string Help = """
		user:name or user:id          only this player's scores
		beatmap:id                    only this beatmap's scores
		Other words are read as a beatmap search (see the Beatmaps tab, ?)
		Example: user:bob beatmap:75
		""";

	private readonly ListPage<ScoreItem> _page;
	private bool _opened;

		public ScoresTab(IServiceProvider services, UserDetails users, BeatmapDetails beatmaps, Glyphs glyphs,
		ShellKit kit, CliOptions options, TimeProvider clock, Action<Action> post, ILogger logger)
	{
		_page = new ListPage<ScoreItem>("Scores", Help, Columns(users),
			s => s.Id.ToString(CultureInfo.InvariantCulture), new ScoresSource(services), s => Detail(s, users, beatmaps),
			glyphs, kit, options.NoColor, clock, post, logger);
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

	public void FocusSearch()
	{
		_page.FocusSearch();
	}

	public void ShowHelp()
	{
		_page.ShowHelp();
	}

	internal static IReadOnlyList<Column<ScoreItem>> Columns(UserDetails? users)
	{
		return
		[
			new Column<ScoreItem>("ID", 8, s => s.Id.ToString(CultureInfo.InvariantCulture)) { Right = true },
			new Column<ScoreItem>("Player", 0, s => s.Player, s => UserInk.Of((UserPrivileges)s.Mask))
			{
				Link = users is null ? null : s => s.UserId is { } id ? (s.Player, () => users.OfAsync(UserReference.ById(id))) : null
			},
			new Column<ScoreItem>("Beatmap", 0, s => s.Beatmap) { Weight = 3 },
			new Column<ScoreItem>("Mods", 12, s => s.Mods, _ => Ink.Info),
			new Column<ScoreItem>("Score", 12, s => s.Score.ToString("N0", CultureInfo.InvariantCulture)) { Right = true },
			new Column<ScoreItem>("Acc", 8, s => $"{s.Accuracy.ToString("0.00", CultureInfo.InvariantCulture)}%") { Right = true },
			new Column<ScoreItem>("Grade", 6, s => GradeText(s.Grade), s => Rank(s.Grade)),
			new Column<ScoreItem>("Time", 17, s => s.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm",
				CultureInfo.InvariantCulture), _ => Ink.Muted)
		];
	}

	private static string GradeText(Grade grade)
	{
		return grade switch { Grade.Sh => "SH", Grade.Xh => "SSH", Grade.X => "SS", _ => grade.ToString() };
	}

	private static Ink Rank(Grade grade)
	{
		return grade switch
		{
			Grade.X or Grade.Xh or Grade.S or Grade.Sh => Ink.GradeTop,
			Grade.A => Ink.GradeA,
			Grade.B => Ink.GradeB,
			Grade.C => Ink.GradeC,
			Grade.D or Grade.F => Ink.GradeD,
			_ => Ink.Muted
		};
	}

	internal static DetailView Detail(ScoreItem score, UserDetails users, BeatmapDetails beatmaps)
	{
		var fields = new List<(string, string, Ink)>
		{
			("ID", score.Id.ToString(CultureInfo.InvariantCulture), Ink.Text), ("Player", score.Player, UserInk.Of((UserPrivileges)score.Mask)),
			("Beatmap", score.Beatmap, Ink.Text), ("Mods", score.Mods, Ink.Info),
			("Score", score.Score.ToString("N0", CultureInfo.InvariantCulture), Ink.Text),
			("Accuracy", $"{score.Accuracy.ToString("0.00", CultureInfo.InvariantCulture)}%", Ink.Text),
			("Grade", GradeText(score.Grade), Rank(score.Grade))
		};
		var actions = new List<DetailAction>();
		if (score.UserId is { } user)
			actions.Add(DetailAction.OpenAsync("User", () => users.OfAsync(UserReference.ById(user))));
		if (score.BeatmapId is { } map) actions.Add(DetailAction.OpenAsync("Beatmap", () => beatmaps.OfAsync(map)));
		return new DetailView($"{score.Player} on {score.Beatmap}", fields, actions);
	}
}

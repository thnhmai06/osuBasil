using System.Globalization;
using System.Text.RegularExpressions;
using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Scores.Messages;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Domain.Scores;
using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Shell;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal.UI;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>One score in the scores table.</summary>
/// <param name="Id">The score id.</param>
/// <param name="UserId">The id of the player, when known.</param>
/// <param name="Player">The name of the player, or the id when the account is gone.</param>
/// <param name="BeatmapId">The id of the beatmap played, when the server knows it.</param>
/// <param name="Beatmap">The name of the beatmap played, or its hash when the server does not know it.</param>
/// <param name="Mods">The mods, as text.</param>
/// <param name="Score">The total score.</param>
/// <param name="Accuracy">The accuracy, as a percentage.</param>
/// <param name="Grade">The grade earned.</param>
/// <param name="Time">When the play happened.</param>
internal sealed record ScoreItem(int Id, int? UserId, string Player, int? BeatmapId, string Beatmap, string Mods,
	int Score, double Accuracy, Grade Grade, DateTimeOffset Time);

/// <summary>Reads the newest scores that match <c>user:&lt;id|name&gt; beatmap:&lt;id|query&gt;</c>.</summary>
internal sealed partial class ScoresSource(IServiceProvider services) : IListSource<ScoreItem>
{
	/// <inheritdoc />
	public async Task<IReadOnlyList<SectionData<ScoreItem>>> LoadAsync(string query, int limit,
		CancellationToken ct)
	{
		var (user, beatmapId, beatmaps) = Parse(query);
		var sender = services.GetRequiredService<ISender>();
		var listing = await sender.Send(new SearchScores(user, beatmapId, beatmaps, new PageRequest(0, limit)), ct);
		if (listing.Scores is not { } page) return [new SectionData<ScoreItem>("Scores", [], false)];

		var players = new Dictionary<int, string>();
		var maps = new Dictionary<string, (int? Id, string Name)>();
		var items = new List<ScoreItem>();
		foreach (var score in page.Items)
		{
			var data = score.Value;
			var player = "-";
			if (data.UserId is { } id && !players.TryGetValue(id, out player))
			{
				var found = await sender.Send(new GetUser(UserReference.ById(id)), ct);
				players[id] = player = found?.Value.Name ?? id.ToString(CultureInfo.InvariantCulture);
			}

			var map = (Id: (int?)null, Name: "-");
			if (data.BeatmapHash is { } hash && !maps.TryGetValue(hash.HashValue, out map))
			{
				var found = await sender.Send(new FindBeatmapByHash(hash), ct);
				maps[hash.HashValue] = map = found is null ? (null, hash.HashValue) : (found.Id, found.Name);
			}

			items.Add(new ScoreItem(score.Id, data.UserId, player!, map.Id, map.Name, data.Mods.ToString(),
				data.LastState.TotalScore, data.Accuracy, data.Grade, data.Timestamp));
		}

		return [new SectionData<ScoreItem>("Scores", items, page.HasNext)];
	}

	/// <summary>Reads the search text; a beatmap value with spaces is quoted, and any other text is ignored.</summary>
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

/// <summary>The scores, newest first, with a jump to the beatmap or the player of each.</summary>
internal sealed class ScoresTab : ITabView
{
	private readonly ListPage<ScoreItem> _page;
	private bool _opened;

	/// <summary>Creates the tab.</summary>
	/// <param name="openIn">Jumps to a tab and selects the item with the given id there.</param>
	public ScoresTab(IServiceProvider services, Action<DashboardTab, string> openIn, Glyphs glyphs,
		CliOptions options, TimeProvider clock, Action<Action> post, ILogger logger)
	{
		_page = new ListPage<ScoreItem>("Scores",
		[
			new Column<ScoreItem>("ID", 8, s => s.Id.ToString(CultureInfo.InvariantCulture)),
			new Column<ScoreItem>("Player", 18, s => s.Player),
			new Column<ScoreItem>("Beatmap", 0, s => s.Beatmap),
			new Column<ScoreItem>("Mods", 12, s => s.Mods, _ => Ink.Info),
			new Column<ScoreItem>("Score", 12, s => s.Score.ToString("N0", CultureInfo.InvariantCulture)),
			new Column<ScoreItem>("Acc", 8, s => $"{s.Accuracy.ToString("0.00", CultureInfo.InvariantCulture)}%"),
			new Column<ScoreItem>("Grade", 6, s => GradeText(s.Grade), s => Rank(s.Grade)),
			new Column<ScoreItem>("Time", 17, s => s.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm",
				CultureInfo.InvariantCulture), _ => Ink.Muted)
		], s => s.Id.ToString(CultureInfo.InvariantCulture), new ScoresSource(services), s => Detail(s, openIn),
			glyphs, options.NoColor, clock, post, logger);
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

	private static string GradeText(Grade grade)
	{
		return grade switch { Grade.Sh => "SH", Grade.Xh => "SSH", Grade.X => "SS", _ => grade.ToString() };
	}

	private static Ink Rank(Grade grade)
	{
		return grade switch
		{
			Grade.X or Grade.Xh or Grade.S or Grade.Sh => Ink.Accent,
			Grade.A => Ink.Success,
			Grade.B => Ink.Info,
			Grade.C => Ink.Warning,
			Grade.D or Grade.F => Ink.Error,
			_ => Ink.Muted
		};
	}

	private static DetailView Detail(ScoreItem score, Action<DashboardTab, string> openIn)
	{
		var fields = new List<(string, string, Ink)>
		{
			("ID", score.Id.ToString(CultureInfo.InvariantCulture), Ink.Text), ("Player", score.Player, Ink.Text),
			("Beatmap", score.Beatmap, Ink.Text), ("Mods", score.Mods, Ink.Info),
			("Score", score.Score.ToString("N0", CultureInfo.InvariantCulture), Ink.Text),
			("Accuracy", $"{score.Accuracy.ToString("0.00", CultureInfo.InvariantCulture)}%", Ink.Text),
			("Grade", GradeText(score.Grade), Rank(score.Grade))
		};
		var actions = new List<DetailAction>();
		if (score.BeatmapId is { } map)
			actions.Add(new DetailAction("Open beatmap", () => Open(openIn, DashboardTab.Beatmaps, map)));
		if (score.UserId is { } user)
			actions.Add(new DetailAction("Open user", () => Open(openIn, DashboardTab.Users, user)));
		return new DetailView(fields, actions);
	}

	private static Task Open(Action<DashboardTab, string> openIn, DashboardTab tab, int id)
	{
		openIn(tab, id.ToString(CultureInfo.InvariantCulture));
		return Task.CompletedTask;
	}
}

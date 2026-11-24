using Basil.Application.Sessions.Registry;
using System.Globalization;
using Basil.Application.Beatmaps.Messages;
using Basil.Domain.Beatmaps;
using Basil.Host.Cli.Dashboard.Shell;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Cli.Dashboard.Tabs;

internal sealed class BeatmapDetails(IServiceProvider services, CommandActions actions, Dialogs dialogs)
{
	// The statuses an operator can give a set; the others describe sets the server does not publish.
	private static readonly string[] Statuses =
	[
		nameof(BeatmapsetStatus.Pending), nameof(BeatmapsetStatus.Ranked), nameof(BeatmapsetStatus.Approved),
		nameof(BeatmapsetStatus.Qualified), nameof(BeatmapsetStatus.Loved)
	];

	public static string Date(DateTimeOffset time)
	{
		return time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
	}

	public static string Number(double value)
	{
		return value.ToString("0.##", CultureInfo.InvariantCulture);
	}

	public static Ink Status(BeatmapsetStatus status)
	{
		return status is BeatmapsetStatus.Ranked or BeatmapsetStatus.Approved ? Ink.MapRanked
			: status is BeatmapsetStatus.Loved ? Ink.MapLoved
			: status is BeatmapsetStatus.Qualified ? Ink.MapQualified
			: Ink.MapPending;
	}

	public async Task<DetailView> OfAsync(int id)
	{
		var sender = services.GetRequiredService<ISender>();
		if (await sender.Send(new GetBeatmap(null, id)) is not { } map)
			return new DetailView(id.ToString(CultureInfo.InvariantCulture), [("Beatmap", "not found", Ink.Warning)],
				[]);

		var actor = await services.GetRequiredService<BotSessions>().OperateAsync();
		var set = await sender.Send(new GetBeatmapset(actor, map.Beatmapset.Id));
		return Of(BeatmapsSource.Item(map, map.Beatmapset, set?.Beatmaps.Count ?? 1));
	}

	public DetailView Of(BeatmapItem map)
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
		var set = map.SetId;
		var actionList = new List<DetailAction>
		{
			new("Set status", () => dialogs.Choose($"Status of beatmapset {set}", Statuses,
				map.Status.ToString(), status => actions.Run($"beatmapsets {set} set --status {status}"))),
			new(map.Private ? "Set public" : "Set private",
				() => actions.Run($"beatmapsets {set} set --visible {(map.Private ? "true" : "false")}")),
			new(map.Locked ? "Unlock" : "Lock",
				() => actions.Run($"beatmapsets {set} set --locked {(map.Locked ? "false" : "true")}")),
			new("Delete beatmapset", () => dialogs.Confirm("Delete beatmapset",
				$"Delete beatmapset {set} with its {map.Versions} difficulties? The set and its files are removed from the server " +
				"and cannot be restored; scores keep their record. A locked set cannot be deleted.", "Delete",
				() => actions.Run($"beatmapsets {set} delete")), DetailActionKind.Destructive)
		};
		return new DetailView(map.Name.Length > 0 ? map.Name : map.Version, fields, actionList)
			{ Reload = () => OfAsync(map.Id) };
	}
}
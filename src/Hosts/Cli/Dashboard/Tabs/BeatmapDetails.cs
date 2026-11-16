using System.Globalization;
using Basil.Application.Beatmaps.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Beatmaps;
using Basil.Host.Cli.Dashboard.Shell;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>Builds the detail of a beatmap, the one the Beatmaps tab and a score show.</summary>
internal sealed class BeatmapDetails(IServiceProvider services, ConsoleTab console, CliOptions options)
{
	/// <summary>Writes a date without its time.</summary>
	public static string Date(DateTimeOffset time)
	{
		return time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
	}

	/// <summary>Writes a difficulty value with at most two decimals.</summary>
	public static string Number(double value)
	{
		return value.ToString("0.##", CultureInfo.InvariantCulture);
	}

	/// <summary>Tells the kind of text for the status of a beatmapset.</summary>
	public static Ink Status(BeatmapsetStatus status)
	{
		return status is BeatmapsetStatus.Ranked or BeatmapsetStatus.Approved ? Ink.MapRanked
			: status is BeatmapsetStatus.Loved ? Ink.MapLoved
			: status is BeatmapsetStatus.Qualified ? Ink.MapQualified
			: Ink.MapPending;
	}

	/// <summary>Reads a beatmap and builds its detail.</summary>
	/// <param name="id">The beatmap id.</param>
	public async Task<DetailView> OfAsync(int id)
	{
		var sender = services.GetRequiredService<ISender>();
		if (await sender.Send(new GetBeatmap(null, id)) is not { } map)
			return new DetailView(id.ToString(CultureInfo.InvariantCulture), [("Beatmap", "not found", Ink.Warning)], []);

		var actor = await services.GetRequiredService<UserRegistry>().OperateAsync();
		var set = await sender.Send(new GetBeatmapset(actor, map.Beatmapset.Id));
		return Of(BeatmapsSource.Item(map, map.Beatmapset, set?.Beatmaps.Count ?? 1));
	}

	/// <summary>Builds the detail of a beatmap that is already read.</summary>
	/// <param name="map">The beatmap.</param>
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
		if (!options.Editable) return new DetailView(map.Id.ToString(), fields, []);

		var set = map.SetId;
		return new DetailView(map.Id.ToString(), fields,
		[
			new DetailAction(map.Private ? "Set public" : "Set private",
				() => console.RunCommand($"beatmapsets {set} set --visible {(map.Private ? "true" : "false")}")),
			new DetailAction(map.Locked ? "Unlock" : "Set locked",
				() => console.RunCommand($"beatmapsets {set} set --locked {(map.Locked ? "false" : "true")}"))
		]);
	}
}

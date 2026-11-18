using System.Globalization;
using Basil.Application.Users.Objects;
using Basil.Domain.Users;
using Basil.Host.Cli.Dashboard.Shell;

namespace Basil.Host.Cli.Dashboard.Tabs;

internal sealed class RoomDetails(UserDetails users, CommandActions actions, Action<string> openChat, Dialogs dialogs)
{
	private const string Settings = "Settings ›";
	private const string Manage = "Manage ›";

	public DetailView Of(RoomItem room)
	{
		var id = room.Id;
		var fields = new List<(string, string, Ink)>
		{
			("ID", id.ToString(CultureInfo.InvariantCulture), Ink.Text), ("Name", room.Name, Ink.Text),
			("State", room.State, LobbyTab.State(room)),
			("Players", $"{room.Players}/{room.Size}", Ink.Text),
			("Creator", room.Creator, UserInk.Of((UserPrivileges)room.CreatorMask)),
			("Referees", room.Referees.Length == 0 ? "-" : room.Referees, Ink.Text)
		};
		if (room.Rules is { } rules)
		{
			fields.Add(("Beatmap", rules.Map.Length == 0 ? "-" : rules.Map, Ink.Text));
			fields.Add(("Mode", rules.Mode, Ink.Text));
			fields.Add(("Mods", $"{rules.Mods}{(rules.Freemods ? " (free mods)" : string.Empty)}", Ink.Text));
			fields.Add(("Teams", $"{rules.TeamType}, {rules.WinCondition}", Ink.Text));
			fields.Add(("Private", rules.Private ? "yes" : "no", rules.Private ? Ink.Warning : Ink.Text));
			fields.Add(("Password", rules.HasPassword ? "set" : "none", Ink.Text));
		}

		fields.AddRange(room.Slots.Select(slot =>
			($"Slot {slot.Index + 1}", $"{slot.Player}{(slot.Host ? " (H)" : string.Empty)}  {slot.Status}", Ink.Text)));
		var list = new List<DetailAction>();
		if (room.Host.Length > 0)
			list.Add(DetailAction.OpenAsync("Host", () => users.OfAsync(UserReference.ByName(room.Host))));
		list.Add(DetailAction.OpenAsync("Creator", () => users.OfAsync(UserReference.ByName(room.Creator))));
		if (room.Slots.Count > 0) list.Add(DetailAction.Open("Players", () => Players(room)));
		if (!room.Open) return new DetailView(room.Name, fields, list);

		list.Add(new DetailAction("Chat…", () => openChat($"#mp_{id}"), DetailActionKind.Navigate));
		if (room.State == "playing")
			list.Add(new DetailAction("Abort round", () => dialogs.Confirm("Abort round",
				$"Abort the round in progress in room {id} ({room.Name})? The round ends for every player and is marked aborted.",
				"Abort", () => Run(id, "abort")), DetailActionKind.Destructive));
		else if (room.CanStartRound)
			list.Add(new DetailAction("Start round", () => AskSeconds(id, "Start round", "start", "0")));
		if (room.CanStartTimer)
			list.Add(new DetailAction("Start timer", () => AskSeconds(id, "Start timer", "timer", "30")));
		if (room.CanAbortTimer) list.Add(new DetailAction("Abort timer", () => Run(id, "aborttimer")));
		list.Add(room.Locked
			? new DetailAction("Unlock", () => Run(id, "unlock"))
			: new DetailAction("Lock", () => Run(id, "lock")));
		list.Add(DetailAction.Menu(Settings, SettingsOf(room)));
		list.Add(DetailAction.Menu(Manage, ManageOf(room)));
		list.Add(new DetailAction("Close room", () => dialogs.Confirm("Close room",
			$"Close room {id} ({room.Name})? The room ends and its {room.Players} player(s) are removed from it. " +
			"It cannot be reopened.", "Close room", () => Run(id, "close")), DetailActionKind.Destructive));
		return new DetailView(room.Name, fields, list);
	}

	private List<DetailAction> SettingsOf(RoomItem room)
	{
		var id = room.Id;
		var rules = room.Rules!;
		return
		[
			new("Rename", () => dialogs.Form("Rename room", [new FormField("Name", room.Name)],
				values => Run(id, "name", $"--name {CommandActions.Quote(values[0])}"))),
			new("Size", () => dialogs.Form("Room size", [new FormField("Slots (1-16)", room.Size.ToString(CultureInfo.InvariantCulture))],
				values => Run(id, "size", $"--size {CommandActions.Quote(values[0])}"))),
			new("Teams and winning", () => dialogs.Form("Teams and winning",
				[new FormField("Team type", rules.TeamType), new FormField("Win condition", rules.WinCondition),
					new FormField("Size (optional)")],
				values => Run(id, "set",
					$"--teamType {CommandActions.Quote(values[0])} --winCondition {CommandActions.Quote(values[1])}" +
					(values[2].Length > 0 ? $" --size {CommandActions.Quote(values[2])}" : string.Empty)))),
			new("Password", () => dialogs.Form("Room password", [new FormField("Password (empty removes)", Secret: true)],
				values => Run(id, "password", $"--password {CommandActions.Quote(values[0])}"))),
			new(rules.Private ? "Make public" : "Make private",
				() => Run(id, "private", $"--private {(rules.Private ? "false" : "true")}")),
			new("Beatmap", () => dialogs.Form("Room beatmap", [new FormField("Beatmap id"), new FormField("Mode (optional)")],
				values => Run(id, "map",
					$"--beatmap {CommandActions.Quote(values[0])}" +
					(values[1].Length > 0 ? $" --mode {CommandActions.Quote(values[1])}" : string.Empty)))),
			new("Mods", () => dialogs.Form("Room mods", [new FormField("Mods (HD,DT or None)"), new FormField("Free mods (yes/no)")],
				values => Run(id, "mods",
					$"--mods {CommandActions.Quote(values[0].Length == 0 ? "None" : values[0])}" +
					(values[1].Length > 0 ? $" --freemod {(values[1].StartsWith('y') ? "true" : "false")}" : string.Empty))))
		];
	}

	private List<DetailAction> ManageOf(RoomItem room)
	{
		var id = room.Id;
		var list = new List<DetailAction>();
		if (room.Slots.Count > 0)
		{
			list.Add(new DetailAction("Set host", () => Pick(room, "Set host", "host")));
			list.Add(new DetailAction("Kick player", () => Pick(room, "Kick player", "kick")));
			list.Add(new DetailAction("Ban player", () => Pick(room, "Ban player", "ban")));
			list.Add(new DetailAction("Move player", () => dialogs.Form("Move player",
				[new FormField("Player (id or name)"), new FormField("Slot (1-16)")], values =>
					actions.RunForUser($"rooms {id} move --slot {CommandActions.Quote(values[1])} --user {{0}}", values[0]))));
			list.Add(new DetailAction("Change team", () => dialogs.Form("Change team",
				[new FormField("Player (id or name)"), new FormField("Team (Blue/Red)")], values =>
					actions.RunForUser($"rooms {id} team --team {CommandActions.Quote(values[1])} --user {{0}}", values[0]))));
		}

		if (room.Host.Length > 0) list.Add(new DetailAction("Clear host", () => Run(id, "clearhost")));
		list.Add(new DetailAction("Unban player", () => Ask(id, "Unban player", "unban")));
		list.Add(new DetailAction("Invite player", () => Ask(id, "Invite player", "invite")));
		list.Add(new DetailAction("Add referee", () => Ask(id, "Add referee", "addref")));
		if (room.Referees.Length > 0)
			list.Add(new DetailAction("Remove referee", () => dialogs.Choose("Remove referee",
				room.Referees.Split(", "), null, name => actions.RunForUser($"rooms {id} removeref --user {{0}}", name))));
		return list;
	}

	// One navigation button per seated player, each opening that player's detail.
	private DetailView Players(RoomItem room)
	{
		var fields = room.Slots.Select(slot =>
			($"Slot {slot.Index + 1}", $"{slot.Player}{(slot.Host ? " (H)" : string.Empty)}  {slot.Status}", Ink.Text)).ToList();
		var list = room.Slots.Select(slot => DetailAction.OpenAsync(slot.Player,
			() => users.OfAsync(UserReference.ByName(slot.Player)))).ToList();
		return new DetailView("Players", fields, list);
	}

	private void Run(int room, string verb, string options = "")
	{
		actions.Run($"rooms {room} {verb}{(options.Length == 0 ? string.Empty : " " + options)}");
	}

	private void Pick(RoomItem room, string title, string verb)
	{
		dialogs.Choose(title, [.. room.Slots.Select(slot => slot.Player)], null,
			name => actions.RunForUser($"rooms {room.Id} {verb} --user {{0}}", name));
	}

	private void Ask(int room, string title, string verb)
	{
		dialogs.Form(title, [new FormField("User (id or name)")],
			values => actions.RunForUser($"rooms {room} {verb} --user {{0}}", values[0]));
	}

	private void AskSeconds(int room, string title, string verb, string start)
	{
		dialogs.Form(title, [new FormField("Seconds (0 = now)", start)], values =>
			Run(room, verb, values[0] is "" or "0" && verb == "start" ? string.Empty : $"--seconds {CommandActions.Quote(values[0])}"));
	}
}

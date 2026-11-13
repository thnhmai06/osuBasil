using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Registry;
using Basil.Host.Cli.Dashboard.Shell;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal.UI;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>One room in the lobby table, open or closed.</summary>
/// <param name="Id">The room id (the match id).</param>
/// <param name="Name">The room name.</param>
/// <param name="Players">How many players sit in it.</param>
/// <param name="Size">How many slots are usable.</param>
/// <param name="State">Playing, waiting, locked or closed.</param>
/// <param name="Host">The host, or empty.</param>
/// <param name="Creator">The creator, or empty.</param>
/// <param name="Referees">The referees, as names.</param>
/// <param name="Open">Whether the room is open now, so it can be changed.</param>
/// <param name="Locked">Whether nobody else can join.</param>
/// <param name="Slots">The seated players: slot number, name, whether host, and the slot's status.</param>
internal sealed record RoomItem(int Id, string Name, int Players, int Size, string State, string Host,
	string Creator, string Referees, bool Open, bool Locked, IReadOnlyList<(int Index, string Player, bool Host, string Status)> Slots);

/// <summary>Reads the rooms: the open ones from memory, the closed ones from the match history.</summary>
internal sealed class LobbySource(IServiceProvider services) : IListSource<RoomItem>
{
	/// <inheritdoc />
	public async Task<IReadOnlyList<SectionData<RoomItem>>> LoadAsync(string query, int limit, CancellationToken ct)
	{
		var lobby = services.GetRequiredService<Lobby>();
		var text = query.Trim();
		bool Matches(string name, int id) => text.Length == 0 || name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
		                                     text == id.ToString();
		var open = lobby.Rooms.Values.Where(room => Matches(room.Settings.Name, room.Id)).OrderBy(room => room.Id)
			.Select(room =>
			{
				var host = room.Authority.Host;
				return new RoomItem(room.Id, room.Settings.Name, room.Slots.Count(slot => slot.Player is not null),
					room.Slots.Count(slot => !slot.Locked),
					room.Rounds.InProgress ? "playing" : room.Slots.Locked ? "locked" : "waiting",
					host?.Record.Value.Name ?? string.Empty, room.Authority.Creator.Value.Name,
					string.Join(", ", room.Authority.Referees.Select(referee => referee.Value.Name)), true,
					room.Slots.Locked,
					[
						.. room.Slots.Where(slot => slot.Player is not null).Select(slot => (slot.Index,
							slot.Player!.Record.Value.Name, ReferenceEquals(host, slot.Player.User),
							slot.Status?.ToString() ?? "open"))
					]);
			}).ToList();
		var openIds = open.Select(room => room.Id).ToHashSet();
		var actor = await services.GetRequiredService<UserRegistry>().OperateAsync(ct);
		var (result, found) = await services.GetRequiredService<ISender>()
			.Send(new ListMatches(actor, new MatchQuery(true), new PageRequest(0, limit)), ct);
		var closed = found is null
			? []
			: found.Items.Where(match => !openIds.Contains(match.Id) && Matches(match.Value.Name, match.Id))
				.Select(match => new RoomItem(match.Id, match.Value.Name, 0, 0, "closed", string.Empty,
					match.Value.Creator.Value.Name, string.Empty, false, false, [])).ToList();
		return
		[
			new SectionData<RoomItem>("Open", open, false),
			new SectionData<RoomItem>("Closed", closed, found?.HasNext ?? false)
		];
	}
}

/// <summary>The rooms: the open ones with what the creator and the referees can do, and the ones that ended.</summary>
internal sealed class LobbyTab : ITabView
{
	private readonly ListPage<RoomItem> _page;
	private bool _opened;

	/// <summary>Creates the tab.</summary>
	public LobbyTab(IServiceProvider services, ConsoleTab console, Action<string> openChat, CliOptions options,
		Glyphs glyphs, TimeProvider clock, Action<Action> post, ILogger logger)
	{
		_page = new ListPage<RoomItem>("Lobby",
		[
			new Column<RoomItem>("ID", 8, r => r.Id.ToString()),
			new Column<RoomItem>("Name", 0, r => r.Name),
			new Column<RoomItem>("Players", 9, r => r.Open ? $"{r.Players}/{r.Size}" : "-"),
			new Column<RoomItem>("State", 9, r => r.State, State),
			new Column<RoomItem>("Host", 16, r => r.Host),
			new Column<RoomItem>("Creator", 16, r => r.Creator, _ => Ink.Muted)
		], r => r.Id.ToString(), new LobbySource(services), room => Detail(room, console, openChat, options),
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

	private static Ink State(RoomItem room)
	{
		return room.State switch
		{
			"playing" => Ink.RoomPlaying,
			"locked" => Ink.RoomLocked,
			"closed" => Ink.RoomClosed,
			_ => Ink.RoomOpen
		};
	}

	private static DetailView Detail(RoomItem room, ConsoleTab console, Action<string> openChat, CliOptions options)
	{
		var fields = new List<(string, string, Ink)>
		{
			("ID", room.Id.ToString(), Ink.Text), ("Name", room.Name, Ink.Text), ("State", room.State, State(room)),
			("Creator", room.Creator, Ink.Text), ("Referees", room.Referees.Length == 0 ? "-" : room.Referees, Ink.Text)
		};
		fields.AddRange(room.Slots.Select(slot =>
			($"Slot {slot.Index + 1}", $"{slot.Player}{(slot.Host ? " (H)" : string.Empty)}  {slot.Status}", Ink.Text)));
		if (!options.Editable || !room.Open) return new DetailView(fields, []);

		var id = room.Id;
		var actions = new List<DetailAction>
		{
			new("Open chat", () =>
			{
				openChat($"#mp_{id}");
				return Task.CompletedTask;
			}),
			room.Locked
				? new DetailAction("Unlock", () => Run(console, $"rooms {id} unlock"))
				: new DetailAction("Lock", () => Run(console, $"rooms {id} lock")),
			new("Kick…", () => AskUser(console, "Kick", id, "kick"), true),
			new("Ban…", () => AskUser(console, "Ban", id, "ban"), true),
			new("Set host…", () => AskUser(console, "Set host", id, "host")),
			new("Add referee…", () => AskUser(console, "Add referee", id, "addref")),
			new("Remove referee…", () => AskUser(console, "Remove referee", id, "removeref")),
			new("Close room", () => Run(console, $"rooms {id} close"), true)
		};
		return new DetailView(fields, actions);
	}

	private static Task Run(ConsoleTab console, string line)
	{
		console.RunCommand(line);
		return Task.CompletedTask;
	}

	private static Task AskUser(ConsoleTab console, string title, int room, string verb)
	{
		Dialogs.Form(title, [("User id", string.Empty)], values =>
		{
			if (int.TryParse(values[0], out var user)) console.RunCommand($"rooms {room} {verb} --user {user}");
		});
		return Task.CompletedTask;
	}
}

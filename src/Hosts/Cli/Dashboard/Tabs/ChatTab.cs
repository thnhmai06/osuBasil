using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Users.Registry;
using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Shell;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>One entry of the channel list.</summary>
/// <param name="Name">The channel name, which is the id.</param>
/// <param name="Members">How many members it has, or -1 when unknown.</param>
internal sealed record ChannelItem(string Name, int Members);

/// <summary>One entry of the member list.</summary>
/// <param name="Name">The user name.</param>
/// <param name="Mark">The mark after the name: <c>(H)</c> for the host, <c>(P)</c> for a player in a round.</param>
internal sealed record MemberItem(string Name, string Mark);

/// <summary>A tab whose scrolling view can be sent back to its newest line.</summary>
internal interface IJumpable
{
	/// <summary>Follows the newest line again.</summary>
	void JumpToEnd();
}

/// <summary>
///     The chat: the channels on the left, the messages of the selected channel in the middle with a box to answer as
///     BasilBot, and the members on the right, grouped by role.
/// </summary>
internal sealed class ChatTab : ITabView, ISelectable, IJumpable
{
	private readonly Palette _palette;
	private readonly GroupedList<ChannelItem> _channels;
	private readonly ChatFeed _feed;
	private readonly Glyphs _glyphs;
	private readonly LogControl _log = new LogControl { MaxCapacity = 500 }.WrapText(true);
	private readonly ILogger _logger;
	private readonly GroupedList<MemberItem> _members;
	private readonly CliOptions _options;
	private readonly PromptEditor? _input;
	private readonly IServiceProvider _services;
	private readonly State<string> _title = new("Chat");
	private readonly Func<(int Columns, int Rows)> _size;
	private readonly Grid _grid;
	private readonly State<string> _memberQuery = new(string.Empty);
	private readonly State<bool> _showLeft = new(true);
	private readonly State<bool> _showCenter = new(true);
	private readonly State<bool> _showRight = new(true);
	private string _memberSeen = string.Empty;
	private string _layoutKey = string.Empty;
	private long _feedRevision = -1;
	private bool _open;
	private bool _membersShown;
	private long _lastTick;
	private string _current = string.Empty;
	private long _seen;
	private TerminalApp? _app;

	/// <summary>Creates the tab.</summary>
	public ChatTab(IServiceProvider services, ChatFeed feed, CliOptions options, Glyphs glyphs, Palette palette,
		Func<(int Columns, int Rows)> size, ILogger logger)
	{
		_services = services;
		_feed = feed;
		_options = options;
		_glyphs = glyphs;
		_palette = palette;
		_size = size;
		_logger = logger;
		_channels = new GroupedList<ChannelItem>(
			[new Column<ChannelItem>("Channel", 0, c => c.Name), new Column<ChannelItem>("", 4, c => c.Members < 0 ? string.Empty : $"({c.Members})", _ => Ink.Muted)],
			c => c.Name, glyphs, options.NoColor);
		_members = new GroupedList<MemberItem>(
			[new Column<MemberItem>("Member", 0, m => m.Name), new Column<MemberItem>("", 4, m => m.Mark, _ => Ink.Accent)],
			m => m.Name, glyphs, options.NoColor);
		var left = new Group("Channels").Style(GroupStyle.Rounded).Content(new ScrollViewer(_channels)).Stretch();
		var rows = new List<RowDefinition> { new() { Height = GridLength.Star() } };
		var middle = new Grid().Columns(new ColumnDefinition { Width = GridLength.Star() });
		if (options.Editable)
		{
			_input = new PromptEditor().LineMode(PromptEditorLineMode.SingleLine).PromptMarkup(string.Empty)
				.Placeholder("Chatting as BasilBot").Accepted(Send);
			rows.Add(new RowDefinition { Height = GridLength.Auto });
		}

		rows.Insert(1, new RowDefinition { Height = GridLength.Auto });
		var jump = new Button((glyphs.Ascii ? "v" : "↓") + " Jump to End (Ctrl+End)").Click(JumpToEnd)
			.IsVisible(() => !_log.FollowTail);
		middle.Rows([.. rows]).Cell(_log, 0, 0).Cell(jump, 1, 0);
		if (_input is not null) middle.Cell(_input, 2, 0);
		var center = new Group().Style(GroupStyle.Rounded).Content(middle).TopLeftText(() => _title.Value).Stretch();
		var search = new HStack(new TextBlock(glyphs.Ascii ? "? " : "⌕ "),
			new TextBox().Text(_memberQuery).Placeholder("search").Stretch());
		var right = new Group("Members").Style(GroupStyle.Rounded)
			.Content(new VStack(new ScrollViewer(_members).Stretch(), search).Spacing(0)).Stretch()
			.IsVisible(_showRight);
		left.IsVisible(_showLeft);
		center.IsVisible(_showCenter);
		_grid = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Star(3) }, new ColumnDefinition { Width = GridLength.Star(6) },
				new ColumnDefinition { Width = GridLength.Star(3) })
			.Rows(new RowDefinition { Height = GridLength.Star() })
			.Cell(left, 0, 0).Cell(center, 0, 1).Cell(right, 0, 2);
		View = _grid;
		_channels.KeyHook = _ => false;
		_channels.Activated += _ =>
		{
			if (!Narrow) return;

			_open = true;
			Arrange();
		};
	}

	/// <inheritdoc />
	public Visual View { get; }

	/// <inheritdoc />
	public void Attach(TerminalApp app)
	{
		_app = app;
		app.AddGlobalCommand(new Command
		{
			Id = "chat.back", LabelMarkup = "Back", Gesture = new KeyGesture(TerminalKey.Escape),
			CanExecute = _ => Narrow && _open && Active, Execute = _ =>
			{
				_open = false;
				_membersShown = false;
				Arrange();
			}
		});
		app.AddGlobalCommand(new Command
		{
			Id = "chat.back.alt", LabelMarkup = "Back", Gesture = new KeyGesture(TerminalKey.Left, TerminalModifiers.Alt),
			CanExecute = _ => Narrow && _open && Active, Execute = _ =>
			{
				_open = false;
				_membersShown = false;
				Arrange();
			}
		});
		app.AddGlobalCommand(new Command
		{
			Id = "chat.members", LabelMarkup = "Members", Gesture = new KeyGesture('b', TerminalModifiers.Ctrl),
			CanExecute = _ => Active, Execute = _ =>
			{
				_membersShown = !_membersShown;
				Arrange();
			}
		});
	}

	/// <summary>Follows the newest message again.</summary>
	public void JumpToEnd()
	{
		_log.ScrollToTail();
	}

	/// <inheritdoc />
	public void Activate()
	{
		_lastTick = Environment.TickCount64;
		Refresh();
		Arrange();
	}

	/// <inheritdoc />
	public void Tick()
	{
		_lastTick = Environment.TickCount64;
		if (_feed.Revision != _feedRevision) Refresh();
		if (_memberQuery.Value != _memberSeen) Members();
		Arrange();
		var selected = _channels.HasSelection ? _channels.Selected!.Name : string.Empty;
		if (selected != _current)
		{
			_current = selected;
			_seen = 0;
			_log.Clear();
			_title.Value = _current.Length == 0 ? "Chat" : Palette.Plain(_current);
			Members();
		}

		var fresh = _feed.Since(_current, _seen, out var total);
		foreach (var line in fresh) _log.AppendMarkupLine(Format(line));
		_seen = total;
	}

	/// <inheritdoc />
	public void Refresh()
	{
		_feedRevision = _feed.Revision;
		var lobby = _services.GetRequiredService<Lobby>();
		var general = _services.GetRequiredService<GeneralChannelRegistry>().All;
		var names = _feed.Channels().Union(general.Keys, StringComparer.OrdinalIgnoreCase)
			.Union(lobby.Rooms.Keys.Select(id => $"#mp_{id}"), StringComparer.OrdinalIgnoreCase).Order().ToList();
		ChannelItem Item(string name) => new(name,
			general.TryGetValue(name, out var channel) ? channel.Members.Count : -1);
		_channels.SetSections(
		[
			new Section<ChannelItem>("Channels", names.Count(n => IsGeneral(n)).ToString(), [.. names.Where(IsGeneral).Select(Item)]),
			new Section<ChannelItem>("Multiplayer", names.Count(n => n.StartsWith("#mp_")).ToString(), [.. names.Where(n => n.StartsWith("#mp_")).Select(Item)]),
			new Section<ChannelItem>("DMs", names.Count(n => !n.StartsWith('#')).ToString(), [.. names.Where(n => !n.StartsWith('#')).Select(Item)])
		]);
		Members();
	}

	/// <inheritdoc />
	public void Select(string id)
	{
		Refresh();
		_channels.Select(id);
	}

	// Tick runs about ten times a second only while the tab is in view.
	private bool Active => Environment.TickCount64 - _lastTick < 500;

	private bool Narrow => _size().Columns < 80;

	// Narrow: one pane at a time (channels, then the conversation, or its members); wider: the three panes.
	private void Arrange()
	{
		var narrow = Narrow;
		var members = narrow ? _open && _membersShown : _membersShown || _size().Columns >= 100;
		var left = !narrow || !_open;
		var center = !narrow || (_open && !_membersShown);
		var key = $"{left}{center}{members}{narrow}{_open}";
		if (key == _layoutKey) return;

		_layoutKey = key;
		GridLength Width(bool shown, double star) => shown ? GridLength.Star(star) : GridLength.Fixed(0);
		_showLeft.Value = left;
		_showCenter.Value = center;
		_showRight.Value = members;
		_grid.Columns(new ColumnDefinition { Width = Width(left, narrow ? 1 : 3) },
			new ColumnDefinition { Width = Width(center, narrow ? 1 : 6) },
			new ColumnDefinition { Width = Width(members, narrow ? 1 : 3) });
		if (_app is null) return;

		if (center && _input is not null) _app.Focus(_input);
		else if (left) _app.Focus(_channels);
	}

	private static bool IsGeneral(string name)
	{
		return name.StartsWith('#') && !name.StartsWith("#mp_");
	}

	private string Format(ChatLine line)
	{
		var ink = line.Author.Equals("BasilBot", StringComparison.OrdinalIgnoreCase) ? Ink.ChatBot : Ink.Text;
		return $"{_palette.Wrap(Ink.Muted, line.Time.ToLocalTime().ToString("H:mm"))} " +
		       $"{_palette.Wrap(ink, line.Author + ":")} {_palette.Wrap(Ink.Text, line.Text)}";
	}

	private void Members()
	{
		_memberSeen = _memberQuery.Value;
		var referees = new List<MemberItem>();
		var players = new List<MemberItem>();
		var others = new List<MemberItem>();
		if (_current.StartsWith("#mp_") && int.TryParse(_current[4..], out var id) &&
		    _services.GetRequiredService<Lobby>().Rooms.TryGetValue(id, out var room))
		{
			var host = room.Authority.Host;
			referees.Add(new MemberItem(room.Authority.Creator.Value.Name, string.Empty));
			referees.AddRange(room.Authority.Referees.Select(r => new MemberItem(r.Value.Name, string.Empty)));
			players.AddRange(room.Slots.Where(slot => slot.Player is not null).Select(slot => new MemberItem(
				slot.Player!.Record.Value.Name,
				ReferenceEquals(host, slot.Player.User) ? "(H)" : room.Rounds.InProgress ? "(P)" : string.Empty)));
		}
		else if (_services.GetRequiredService<GeneralChannelRegistry>().Find(_current) is { } channel)
		{
			others.AddRange(channel.Members.Select(c => new MemberItem(c.User.Record.Value.Name, string.Empty))
				.DistinctBy(m => m.Name).OrderBy(m => m.Name));
		}

		var query = _memberSeen.Trim();
		List<MemberItem> Match(List<MemberItem> items)
		{
			return query.Length == 0 ? items : [.. items.Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase))];
		}

		referees = Match(referees);
		players = Match(players);
		others = Match(others);
		_members.SetSections(
		[
			new Section<MemberItem>("Referees", referees.Count.ToString(), referees),
			new Section<MemberItem>("Players", players.Count.ToString(), players),
			new Section<MemberItem>("Others", others.Count.ToString(), others)
		]);
	}

	private void Send()
	{
		var text = _input?.Text?.Trim() ?? string.Empty;
		if (_input is not null) _input.Text = string.Empty;
		if (text.Length == 0 || _current.Length == 0) return;

		var channel = _current;
		_ = Task.Run(async () =>
		{
			try
			{
				var actor = await _services.GetRequiredService<UserRegistry>().OperateAsync(CancellationToken.None);
				ChatTarget target;
				if (!channel.StartsWith('#'))
				{
					var recipient = _services.GetRequiredService<UserRegistry>().Online.Values
						.FirstOrDefault(u => u.Record.Value.Name.Equals(channel, StringComparison.OrdinalIgnoreCase));
					if (recipient is null)
					{
						_logger.LogWarning("{User} is not online, so the message was not sent", channel);
						return;
					}

					target = new ChatTarget.Private(recipient.Record.Id);
				}
				else
				{
					target = channel.StartsWith("#mp_") && int.TryParse(channel[4..], out var room)
						? new ChatTarget.Room(room)
						: new ChatTarget.General(channel);
				}
				await _services.GetRequiredService<ISender>().Send(new PostChatMessage(actor, target, text));
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "The chat message could not be sent");
			}
		});
	}
}

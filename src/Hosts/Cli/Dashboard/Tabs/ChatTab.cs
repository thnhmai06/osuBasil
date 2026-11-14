using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Users.Objects.Connections;
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
	private readonly State<string> _channelQuery = new(string.Empty);
	private readonly State<string> _memberQuery = new(string.Empty);
	private readonly TextBox _channelSearch;
	private readonly TextBox _memberSearch;
	private readonly State<bool> _showLeft = new(true);
	private readonly State<bool> _showCenter = new(true);
	private readonly State<bool> _showRight = new(true);
	private readonly HashSet<string> _openedDms = new(StringComparer.OrdinalIgnoreCase);
	private List<bool> _channelRows = [];
	private List<bool> _memberRows = [];
	private string _firstChannel = string.Empty;
	private string _firstMember = string.Empty;
	private string _channelSeen = string.Empty;
	private string _memberSeen = string.Empty;
	private string _layoutKey = string.Empty;
	private long _feedRevision = -1;
	private bool _open;
	private bool _membersShown;
	private bool _focusInput;
	private bool _stripSlash;
	private long _lastTick;
	private string _current = string.Empty;
	private long _seen;
	private Task<OperatorConnection>? _operator;
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
			[new Column<ChannelItem>("Channel", 0, c => c.Name), new Column<ChannelItem>("Users", 5, c => c.Members < 0 ? string.Empty : $"({c.Members})", _ => Ink.Muted)],
			c => c.Name, glyphs, options.NoColor);
		_members = new GroupedList<MemberItem>(
			[new Column<MemberItem>("Member", 0, m => m.Name), new Column<MemberItem>("", 4, m => m.Mark, _ => Ink.Accent)],
			m => m.Name, glyphs, options.NoColor);
		_channelSearch = new TextBox().Text(_channelQuery).Placeholder("search");
		_memberSearch = new TextBox().Text(_memberQuery).Placeholder("search");
		_channelSearch.IsTabStop = false;
		_memberSearch.IsTabStop = false;
		var left = new Group("Channels").Style(GroupStyle.Rounded)
			.Content(new VStack(SearchRow(_channelSearch), new ScrollViewer(_channels).IsTabStop(false).PointerPressed((_, e) => OnListClick(e, true)).Stretch()).Spacing(0)).Stretch();
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
		jump.IsTabStop = false;
		_log.IsTabStop = false;
		middle.Rows([.. rows]).Cell(_log, 0, 0).Cell(jump, 1, 0);
		if (_input is not null) middle.Cell(_input, 2, 0);
		var center = new Group().Style(GroupStyle.Rounded).Content(middle).TopLeftText(() => _title.Value).Stretch();
		var right = new Group("Members").Style(GroupStyle.Rounded)
			.Content(new VStack(SearchRow(_memberSearch), new ScrollViewer(_members).IsTabStop(false).PointerPressed((_, e) => OnListClick(e, false)).Stretch()).Spacing(0)).Stretch()
			.IsVisible(_showRight);
		left.IsVisible(_showLeft);
		center.IsVisible(_showCenter);
		_grid = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Star(3) }, new ColumnDefinition { Width = GridLength.Star(6) },
				new ColumnDefinition { Width = GridLength.Star(3) })
			.Rows(new RowDefinition { Height = GridLength.Star() })
			.Cell(left, 0, 0).Cell(center, 0, 1).Cell(right, 0, 2);
		View = _grid;
		_channels.KeyHook = e => ListKey(e, _channelSearch, _channels.Selected?.Name, _firstChannel);
		_members.KeyHook = e => ListKey(e, _memberSearch, _members.Selected?.Name, _firstMember) || MemberKey(e);
		_channels.Activated += _ => OpenSelected();
		AddSearchKeys(_channelSearch, _channelQuery, _channels);
		AddSearchKeys(_memberSearch, _memberQuery, _members);
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
			CanExecute = _ => Narrow && _open && Active, Execute = _ => Back()
		});
		app.AddGlobalCommand(new Command
		{
			Id = "chat.back.alt", LabelMarkup = "Back", Gesture = new KeyGesture(TerminalKey.Left, TerminalModifiers.Alt),
			CanExecute = _ => Narrow && _open && Active, Execute = _ => Back()
		});
		app.AddGlobalCommand(new Command
		{
			Id = "chat.members", LabelMarkup = "Members", Gesture = new KeyGesture(TerminalChar.CtrlB, TerminalModifiers.Ctrl),
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
		Safely(Refresh);
		Arrange();
		FocusDefault();
	}

	/// <inheritdoc />
	public void Tick()
	{
		_lastTick = Environment.TickCount64;
		if (_feed.Revision != _feedRevision || _channelQuery.Value != _channelSeen) Safely(Refresh);
		if (_stripSlash)
		{
			_stripSlash = false;
			if (_channelQuery.Value.StartsWith('/')) _channelQuery.Value = _channelQuery.Value[1..];
			if (_memberQuery.Value.StartsWith('/')) _memberQuery.Value = _memberQuery.Value[1..];
		}

		if (_memberQuery.Value != _memberSeen) Safely(Members);
		Arrange();
		var selected = _channels.HasSelection ? _channels.Selected!.Name : string.Empty;
		if (selected != _current)
		{
			_current = selected;
			_seen = 0;
			_log.Clear();
			_title.Value = _current.Length == 0 ? "Chat" : Palette.Plain(_current);
			Safely(Members);
		}

		if (_focusInput)
		{
			_focusInput = false;
			if (!Narrow) FocusInput();
		}

		var fresh = _feed.Since(_current, _seen, out var total);
		foreach (var line in fresh) _log.AppendMarkupLine(Format(line));
		_seen = total;
	}

	/// <inheritdoc />
	public void Refresh()
	{
		_feedRevision = _feed.Revision;
		_channelSeen = _channelQuery.Value;
		var lobby = _services.GetRequiredService<Lobby>();
		var general = _services.GetRequiredService<GeneralChannelRegistry>().All;
		var query = _channelSeen.Trim();
		var names = _feed.Channels().Union(general.Keys, StringComparer.OrdinalIgnoreCase)
			.Union(lobby.Rooms.Keys.Select(id => $"#mp_{id}"), StringComparer.OrdinalIgnoreCase)
			.Union(_openedDms, StringComparer.OrdinalIgnoreCase)
			.Where(name => query.Length == 0 || name.Contains(query, StringComparison.OrdinalIgnoreCase)).Order().ToList();
		ChannelItem Item(string name) => new(name,
			general.TryGetValue(name, out var channel) ? channel.Members.Count : -1);
		var sections = new List<Section<ChannelItem>>();
		void Add(string title, Func<string, bool> belongs)
		{
			var items = names.Where(belongs).Select(Item).ToList();
			if (items.Count > 0 || query.Length == 0)
				sections.Add(new Section<ChannelItem>(title, items.Count.ToString(), items));
		}

		Add("Channels", IsGeneral);
		Add("Multiplayer", n => n.StartsWith("#mp_"));
		Add("DMs", n => !n.StartsWith('#'));
		_firstChannel = sections.FirstOrDefault(s => s.Items.Count > 0)?.Items[0].Name ?? string.Empty;
		_channelRows = Rows(sections);
		_channels.SetSections(sections);
		Members();
	}

	/// <inheritdoc />
	public void Select(string id)
	{
		if (!id.StartsWith('#')) _openedDms.Add(id);
		_channelQuery.Value = string.Empty;
		Safely(Refresh);
		_channels.Select(id);
		_focusInput = true;
	}

	// Tick runs about ten times a second only while the tab is in view.
	private bool Active => Environment.TickCount64 - _lastTick < 500;

	private bool Narrow => _size().Columns < 80;

	// The server changes its channels and rooms from other threads; a read that meets a change is tried again at the next tick.
	private void Safely(Action read)
	{
		try
		{
			read();
		}
		catch (InvalidOperationException exception)
		{
			_logger.LogDebug(exception, "The chat lists changed while they were read");
		}
	}

	private HStack SearchRow(TextBox search)
	{
		return new HStack(new TextBlock(_glyphs.Ascii ? "? " : "⌕ "), search.Stretch());
	}

	private void Back()
	{
		_open = false;
		_membersShown = false;
		Arrange();
	}

	private void FocusInput()
	{
		if (_app is null) return;

		if (_input is not null) _app.Focus(_input);
		else _app.Focus(_channels);
	}

	private void FocusDefault()
	{
		if (_app is null) return;

		if (Narrow && !_open) _app.Focus(_channels);
		else FocusInput();
	}

	private void OpenSelected()
	{
		if (!_channels.HasSelection) return;

		if (Narrow)
		{
			_open = true;
			Arrange();
		}

		FocusInput();
	}

	private void OpenDirectMessage(MemberItem member)
	{
		if (member.Name.Equals("BasilBot", StringComparison.OrdinalIgnoreCase)) return;

		Select(member.Name);
		if (Narrow)
		{
			_open = true;
			_membersShown = false;
			Arrange();
		}
	}

	// A click on an item opens it: a channel is shown and the message box takes the focus; a member opens a message with them.
	private void OnListClick(PointerEventArgs e, bool channels)
	{
		if (e.Button != TerminalMouseButton.Left) return;

		var onItem = channels
			? IsItemRow(_channelRows, _channels.Scroll.OffsetY, e.LocalY)
			: IsItemRow(_memberRows, _members.Scroll.OffsetY, e.LocalY);
		if (!onItem) return;

		if (channels) _app?.Post(OpenSelected);
		else if (_members.Selected is { } member) _app?.Post(() => OpenDirectMessage(member));
	}

	// The rows of a list, true for a group title, to tell where a click landed.
	private static List<bool> Rows<T>(List<Section<T>> sections)
	{
		return [.. sections.SelectMany(section => new[] { true }.Concat(section.Items.Select(_ => false)))];
	}

	// Mirrors the list: a header line, the title of the group in view when the first row in view is an item, then the rows.
	private static bool IsItemRow(List<bool> rows, int offsetY, int localY)
	{
		if (rows.Count == 0 || localY < 1) return false;

		var offset = Math.Clamp(offsetY, 0, rows.Count - 1);
		var line = localY - 1 - (rows[offset] ? 0 : 1);
		var index = offset + line;
		return line >= 0 && index < rows.Count && !rows[index];
	}

	private bool ListKey(KeyEventArgs e, TextBox search, string? selected, string first)
	{
		if (e.Char == '/' || (e.Char == 'f' && e.Modifiers.HasFlag(TerminalModifiers.Ctrl)))
		{
			// The slash is also delivered as text to the box that takes the focus; Tick removes it.
			_stripSlash = e.Char == '/';
			_app?.Focus(search);
			return true;
		}

		if (e.Key == TerminalKey.Up && selected is not null && selected == first)
		{
			_app?.Focus(search);
			return true;
		}

		return false;
	}

	private bool MemberKey(KeyEventArgs e)
	{
		if (e.Key != TerminalKey.Enter || _members.Selected is not { } member) return false;

		OpenDirectMessage(member);
		return true;
	}

	// Down and Enter go to the list; Esc clears the text, and when there is none goes to the list.
	private void AddSearchKeys<T>(TextBox search, State<string> query, GroupedList<T> list)
	{
		void Add(string name, KeyGesture gesture, Action run)
		{
			search.AddCommand(new Command
			{
				Id = $"chat.search.{search.GetHashCode()}.{name}", LabelMarkup = name, Gesture = gesture, Execute = _ => run()
			});
		}

		Add("down", new KeyGesture(TerminalKey.Down), () => _app?.Focus(list));
		Add("enter", new KeyGesture(TerminalKey.Enter), () => _app?.Focus(list));
		foreach (var gesture in Dialogs.BackGestures)
			Add($"back.{gesture}", gesture, () =>
			{
				if (query.Value.Length > 0) query.Value = string.Empty;
				else _app?.Focus(list);
			});
	}

	// Narrow: one pane at a time (channels, then the conversation, or its members); wider: the three panes.
	private void Arrange()
	{
		var narrow = Narrow;
		var members = narrow ? _open && _membersShown : _membersShown || _size().Columns >= 100;
		// Below 100 columns the members take the place of the channels.
		var left = narrow ? !_open : !(_membersShown && _size().Columns < 100);
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
			referees.AddRange(room.Authority.Referees.ToArray().Select(r => new MemberItem(r.Value.Name, string.Empty)));
			foreach (var slot in room.Slots)
			{
				var player = slot.Player;
				if (player is null) continue;

				players.Add(new MemberItem(player.Record.Value.Name,
					ReferenceEquals(host, player) ? "(H)" : room.Rounds.InProgress ? "(P)" : string.Empty));
			}
		}
		else if (_services.GetRequiredService<GeneralChannelRegistry>().Find(_current) is { } channel)
		{
			others.AddRange(channel.Members.ToArray().Select(c => new MemberItem(c.User.Record.Value.Name, string.Empty))
				.DistinctBy(m => m.Name).OrderBy(m => m.Name));
		}

		var query = _memberSeen.Trim();
		List<MemberItem> Match(List<MemberItem> items)
		{
			return query.Length == 0 ? items : [.. items.Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase))];
		}

		List<Section<MemberItem>> sections =
		[
			.. new[] { ("Referees", Match(referees)), ("Players", Match(players)), ("Others", Match(others)) }
				.Where(group => group.Item2.Count > 0)
				.Select(group => new Section<MemberItem>(group.Item1, group.Item2.Count.ToString(), group.Item2))
		];
		_firstMember = sections.FirstOrDefault()?.Items[0].Name ?? string.Empty;
		_memberRows = Rows(sections);
		_members.SetSections(sections);
	}

	private Task<OperatorConnection> Operator()
	{
		return _operator ??= _services.GetRequiredService<UserRegistry>().OperateAsync(CancellationToken.None);
	}

	private void Send()
	{
		var text = _input?.Text?.Trim() ?? string.Empty;
		if (text.Length == 0 || _current.Length == 0) return;

		var channel = _current;
		_ = Task.Run(async () =>
		{
			try
			{
				var actor = await Operator();
				var sender = _services.GetRequiredService<ISender>();
				ChatTarget target;
				if (!channel.StartsWith('#'))
				{
					var recipient = _services.GetRequiredService<UserRegistry>().Online.Values
						.FirstOrDefault(u => u.Record.Value.Name.Equals(channel, StringComparison.OrdinalIgnoreCase));
					if (recipient is null)
					{
						Report(channel, $"{channel} is not online, so the message was not sent.");
						return;
					}

					target = new ChatTarget.Private(recipient.Record.Id);
				}
				else
				{
					target = channel.StartsWith("#mp_") && int.TryParse(channel[4..], out var room)
						? new ChatTarget.Room(room)
						: new ChatTarget.General(channel);
					// The operator speaks as a member of the channel, like any other connection.
					await sender.Send(new JoinChannel(actor, target));
				}

				var outcome = await sender.Send(new PostChatMessage(actor, target, text));
				if (outcome != Basil.Application.Chat.Objects.ChannelOutcome.Ok)
					Report(channel, $"The message was not sent ({outcome}).");
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "The chat message could not be sent");
				Report(channel, "The message could not be sent; see the log.");
			}
		});
		_input?.Text = string.Empty;
	}

	// Tells the operator in the conversation that a message did not go out; it is not part of the chat itself.
	private void Report(string channel, string text)
	{
		_app?.Post(() =>
		{
			if (channel == _current) _log.AppendMarkupLine(_palette.Wrap(Ink.Error, text));
		});
	}
}

using Basil.Application.Authentication.Persistence;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Basil.Host.Cli.Dashboard.Shell;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>
///     Builds the detail of an account, the one every tab shows when the operator clicks a user's name; its
///     navigation buttons open the sessions of the account inside the same frame.
/// </summary>
/// <remarks>The account and its sessions are read while the frame opens, one row each, on the thread of the interface.</remarks>
internal sealed class UserDetails(IServiceProvider services, ConsoleTab console, CliOptions options, Dialogs dialogs,
	TimeProvider clock, Action<string> openChat)
{
	private const int SessionsShown = 20;

	/// <summary>Reads an account and builds its detail.</summary>
	/// <param name="reference">The account, by id or name.</param>
	public DetailView Of(UserReference reference)
	{
		var record = Read(() => services.GetRequiredService<ISender>().Send(new GetUser(reference)).AsTask());
		if (record is null)
			return new DetailView(reference.Name ?? reference.Id.ToString() ?? string.Empty,
				[("User", "not found", Ink.Warning)], []);

		services.GetRequiredService<UserRegistry>().Online.TryGetValue(record, out var online);
		return Of(UsersSource.Item(services.GetRequiredService<Lobby>(), record, online, clock.GetUtcNow()));
	}

	/// <summary>Builds the detail of an account that is already read.</summary>
	/// <param name="user">The account.</param>
	public DetailView Of(UserItem user)
	{
		var room = user.Room.Length == 0 ? "-" : user.Room;
		var managed = user.Managed.Length == 0 ? string.Empty : $" ({user.Managed})";
		var ink = UserInk.Of((UserPrivileges)user.Mask);
		var fields = new List<(string, string, Ink)>
		{
			("ID", user.Id.ToString(), Ink.Text), ("Name", user.Name, ink), ("Country", user.Country, Ink.Text),
			("Privileges", $"{user.Privileges} ({user.Mask})", ink),
			("Connections", user.Connections.Length == 0 ? "offline" : user.Connections, Ink.Info),
			("Room", room + managed, Ink.Text), ("Activity", user.Activity.Length == 0 ? "-" : user.Activity, Ink.Text)
		};
		var actions = new List<DetailAction> { DetailAction.Open("Sessions", () => Sessions(user)) };
		// The bot is a system account: its sessions can be read, nothing can be done to it.
		if (!options.Editable || user.Id == BasilBot.Id) return new DetailView(user.Name, fields, actions);

		var id = user.Id;
		if (user.Connections.Length > 0) actions.Add(new DetailAction("Send message", () => openChat(user.Name)));
		actions.Add(user.Silenced
			? new DetailAction("Unsilence", () => console.RunCommand($"users {id} unsilence"))
			: new DetailAction("Silence", () => AskSilence(id)));
		actions.Add(new DetailAction("Kick", () => console.RunCommand($"users {id} sessions delete"),
			DetailActionKind.Destructive));
		return new DetailView(user.Name, fields, actions);
	}

	// The newest sessions of the account, live ones first in time; each live one can be closed.
	private DetailView Sessions(UserItem user)
	{
		var sender = services.GetRequiredService<ISender>();
		var (outcome, page) = Read(async () =>
		{
			var actor = await services.GetRequiredService<UserRegistry>().OperateAsync();
			return await sender.Send(new ListUserSessions(actor, UserReference.ById(user.Id),
				new PageRequest(0, SessionsShown), SessionState.Any, null));
		});
		if (outcome != UserOutcome.Ok || page is null)
			return new DetailView("Sessions", [("Sessions", "not available", Ink.Warning)], []);

		var now = clock.GetUtcNow();
		var fields = new List<(string, string, Ink)>();
		var actions = new List<DetailAction>();
		var number = 0;
		foreach (var session in page.Items.Select(record => SessionItem.Of(record, now)))
		{
			number++;
			fields.Add(($"#{number} {session.Kind}",
				$"{session.LastSeen}  since {SessionItem.Time(session.Started)}  {session.Where}",
				session.Live ? Ink.Success : Ink.Text));
			var sid = session.Id;
			if (options.Editable && session.Live)
				actions.Add(new DetailAction($"Close #{number}",
					() => console.RunCommand($"users {user.Id} sessions {sid} delete"), DetailActionKind.Destructive));
		}

		if (fields.Count == 0) fields.Add(("Sessions", "none", Ink.Muted));
		return new DetailView("Sessions", fields, actions);
	}

	// The frame needs the data at once. The read runs off the interface thread, because its continuations would
	// otherwise wait for the thread that is waiting for them.
	private static T Read<T>(Func<Task<T>> read)
	{
		return Task.Run(read).GetAwaiter().GetResult();
	}

	private void AskSilence(int id)
	{
		dialogs.Form("Silence", [("Duration", "30m"), ("Reason", string.Empty)], values =>
		{
			var reason = values[1].Length == 0 ? string.Empty : $" --reason \"{values[1].Replace("\"", "'")}\"";
			console.RunCommand($"users {id} silence --duration {values[0]}{reason}");
		});
	}
}

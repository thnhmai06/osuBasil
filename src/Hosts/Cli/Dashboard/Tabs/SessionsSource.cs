using Basil.Application.Authentication.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Basil.Host.Cli.Dashboard.Shell;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>
///     Reads the sessions: with no search text, the ones open now; with a text, the history of the accounts whose name
///     matches, read like the <c>users {id} sessions list</c> command does.
/// </summary>
internal sealed class SessionsSource(IServiceProvider services, TimeProvider clock) : IListSource<SessionItem>
{
	private const int HistoryUsers = 10;
	private const int HistoryPerUser = 20;

	/// <inheritdoc />
	public async Task<IReadOnlyList<SectionData<SessionItem>>> LoadAsync(string query, int limit, CancellationToken ct)
	{
		var registry = services.GetRequiredService<UserRegistry>();
		var now = clock.GetUtcNow();
		var text = query.Trim();
		var items = new List<SessionItem>();
		if (text.Length == 0)
		{
			foreach (var user in registry.Online.Values.Where(user => !user.Record.IsBasilBot()))
			foreach (var connection in user.Connections.Where(connection => !connection.IsServerOwned))
				items.Add(SessionItem.Of(connection.Snapshot(), now) with { Live = true });
		}
		else
		{
			var sender = services.GetRequiredService<ISender>();
			var actor = await registry.OperateAsync(ct);
			var found = await sender.Send(new SearchUsers(actor, UserQuery.Parse(text), new PageRequest(0, HistoryUsers)), ct);
			foreach (var record in found.Items.Where(record => !record.IsBasilBot()))
			{
				var (outcome, page) = await sender.Send(new ListUserSessions(actor, UserReference.ById(record.Id),
					new PageRequest(0, HistoryPerUser), SessionState.Any, null), ct);
				if (outcome == UserOutcome.Ok && page is not null) items.AddRange(page.Items.Select(s => SessionItem.Of(s, now)));
			}
		}

		var ordered = items.OrderByDescending(item => item.Live).ThenByDescending(item => item.LastActive).Take(limit).ToList();
		return
		[
			.. SessionItem.Kinds.Select(kind =>
				new SectionData<SessionItem>(SessionItem.KindName(kind), [.. ordered.Where(item => item.Type == kind)], false))
		];
	}
}

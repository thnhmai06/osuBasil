using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Basil.Host.Cli.Dashboard.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>
///     Reads the sessions open now. A search text only filters them, by account name or id, address, client or kind;
///     it never reads the history of an account.
/// </summary>
internal sealed class SessionsSource(IServiceProvider services, TimeProvider clock) : IListSource<SessionItem>
{
	/// <inheritdoc />
	public Task<IReadOnlyList<SectionData<SessionItem>>> LoadAsync(string query, int limit, CancellationToken ct)
	{
		var now = clock.GetUtcNow();
		var text = query.Trim();
		var items = services.GetRequiredService<UserRegistry>().Online.Values.Where(user => !user.Record.IsBasilBot())
			.SelectMany(user => user.Connections.Where(connection => !connection.IsServerOwned))
			.Select(connection => SessionItem.Of(connection.Snapshot(), now) with { Live = true })
			.Where(item => Matches(item, text))
			.OrderByDescending(item => item.LastActive).Take(limit).ToList();
		IReadOnlyList<SectionData<SessionItem>> sections =
		[
			.. SessionItem.Kinds.Select(kind => new SectionData<SessionItem>(SessionItem.KindName(kind),
				[.. items.Where(item => item.Type == kind)], false))
		];
		return Task.FromResult(sections);
	}

	private static bool Matches(SessionItem item, string text)
	{
		return text.Length == 0 || item.UserName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
		       item.UserId.ToString() == text || item.Ip.Contains(text, StringComparison.OrdinalIgnoreCase) ||
		       item.Client.Contains(text, StringComparison.OrdinalIgnoreCase) ||
		       item.Kind.Contains(text, StringComparison.OrdinalIgnoreCase);
	}
}

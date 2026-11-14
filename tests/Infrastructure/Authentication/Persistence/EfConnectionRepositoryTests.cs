using System.Net;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Persistence;
using Basil.Domain.Authentication;
using Basil.Domain.Client;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Authentication.Tests.Persistence;

/// <summary>Contract tests of the trail of connections of sessions.</summary>
[Collection("Persistence")]
public sealed class EfConnectionRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
	private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
	private static readonly IPAddress Home = IPAddress.Parse("192.0.2.10");
	private static readonly IPAddress Work = IPAddress.Parse("192.0.2.20");

	private static CancellationToken Cancel => TestContext.Current.CancellationToken;

	private IConnectionRepository Connections => Get<IConnectionRepository>();

	private ISessionRepository Sessions => Get<ISessionRepository>();

	private async Task<long> SessionAsync(string name = "trail_user", long id = 1, SessionType type = SessionType.Api)
	{
		var user = await Get<IUserRepository>().CreateAsync(new UserData { Name = name });
		await Sessions.CreateOrUpdateAsync(new SessionRecord
		{
			Id = id,
			Value = new SessionData(user, type, T0,
				new SessionFingerprint(Home, type is SessionType.Stable ? Client() : null),
				new Sha256(BitConverter.GetBytes(id)),
				null, T0)
		});
		return id;
	}

	private Task<Page<ConnectionRecord>> ListAsync(long sessionId, int offset = 0, int limit = 10)
	{
		return Connections.ListAsync(sessionId, new PageRequest(offset, limit), Cancel).AsTask();
	}

	[Fact]
	public async Task OpenedConnection_StoredOpen()
	{
		var session = await SessionAsync();

		await Connections.OpenAsync(session, Home, "laptop", T0, Window);

		var entry = Assert.Single((await ListAsync(session)).Items);
		Assert.Equal(session, entry.Value.SessionId);
		Assert.Equal(Home, entry.Value.Ip);
		Assert.Equal("laptop", entry.Value.ClientInfo);
		Assert.Equal(T0, entry.Value.OpenedAt);
		Assert.Equal(T0, entry.Value.LastActiveAt);
		Assert.Null(entry.Value.ClosedAt);
		Assert.Null(entry.Value.CloseReason);
	}

	[Fact]
	public async Task Closing_StoresLastUse()
	{
		var session = await SessionAsync();
		await Connections.OpenAsync(session, Home, null, T0, Window);

		await Connections.CloseAsync(session, T0.AddMinutes(3), T0.AddMinutes(4), SessionEndReason.TimedOut);

		var entry = Assert.Single((await ListAsync(session)).Items);
		Assert.Equal(T0.AddMinutes(3), entry.Value.LastActiveAt);
		Assert.Equal(T0.AddMinutes(4), entry.Value.ClosedAt);
		Assert.Equal(SessionEndReason.TimedOut, entry.Value.CloseReason);
	}

	[Fact]
	public async Task Closing_AlreadyClosedUnchanged()
	{
		var session = await SessionAsync();
		await Connections.OpenAsync(session, Home, null, T0, Window);
		await Connections.CloseAsync(session, T0, T0.AddMinutes(1), SessionEndReason.LoggedOut);

		await Connections.CloseAsync(session, T0.AddHours(2), T0.AddHours(3), SessionEndReason.Revoked);

		var entry = Assert.Single((await ListAsync(session)).Items);
		Assert.Equal(T0.AddMinutes(1), entry.Value.ClosedAt);
		Assert.Equal(SessionEndReason.LoggedOut, entry.Value.CloseReason);
	}

	[Fact]
	public async Task SameAddressWindow_ContinuesLast()
	{
		var session = await SessionAsync();
		await Connections.OpenAsync(session, Home, null, T0, Window);
		await Connections.CloseAsync(session, T0, T0.AddMinutes(1), SessionEndReason.TimedOut);

		await Connections.OpenAsync(session, Home, null, T0.AddMinutes(4), Window);

		var entry = Assert.Single((await ListAsync(session)).Items);
		Assert.Equal(T0, entry.Value.OpenedAt);
		Assert.Equal(T0.AddMinutes(4), entry.Value.LastActiveAt);
		Assert.Null(entry.Value.ClosedAt);
		Assert.Null(entry.Value.CloseReason);
	}

	[Fact]
	public async Task WindowEndConnection_ContinuesLast()
	{
		var session = await SessionAsync();
		await Connections.OpenAsync(session, Home, null, T0, Window);
		await Connections.CloseAsync(session, T0, T0.AddMinutes(1), SessionEndReason.TimedOut);

		await Connections.OpenAsync(session, Home, null, T0.AddMinutes(1) + Window, Window);

		Assert.Single((await ListAsync(session)).Items);
	}

	[Fact]
	public async Task AfterWindow_NewConnection()
	{
		var session = await SessionAsync();
		await Connections.OpenAsync(session, Home, null, T0, Window);
		await Connections.CloseAsync(session, T0, T0.AddMinutes(1), SessionEndReason.TimedOut);

		await Connections.OpenAsync(session, Home, null, T0.AddMinutes(1) + Window + TimeSpan.FromTicks(1), Window);

		Assert.Equal(2, (await ListAsync(session)).Items.Count);
	}

	[Fact]
	public async Task OtherAddress_NewConnection()
	{
		var session = await SessionAsync();
		await Connections.OpenAsync(session, Home, null, T0, Window);
		await Connections.CloseAsync(session, T0, T0.AddMinutes(1), SessionEndReason.TimedOut);

		await Connections.OpenAsync(session, Work, null, T0.AddMinutes(2), Window);

		var items = (await ListAsync(session)).Items;
		Assert.Equal([Work, Home], items.Select(entry => entry.Value.Ip));
	}

	[Fact]
	public async Task Trail_NewestFirstPaged()
	{
		var session = await SessionAsync();
		foreach (var minutes in new[] { 0, 60, 120 })
		{
			await Connections.OpenAsync(session, Home, null, T0.AddMinutes(minutes), TimeSpan.Zero);
			await Connections.CloseAsync(session, T0.AddMinutes(minutes), T0.AddMinutes(minutes + 1),
				SessionEndReason.LoggedOut);
		}

		var head = await ListAsync(session, 0, 2);
		var tail = await ListAsync(session, 2, 2);

		Assert.Equal([T0.AddMinutes(120), T0.AddMinutes(60)], head.Items.Select(entry => entry.Value.OpenedAt));
		Assert.True(head.HasNext);
		Assert.Equal(T0, Assert.Single(tail.Items).Value.OpenedAt);
		Assert.False(tail.HasNext);
	}

	[Fact]
	public async Task TwoSessionTrails_KeptApart()
	{
		var first = await SessionAsync("first_user");
		var second = await SessionAsync("second_user", 2);
		await Connections.OpenAsync(first, Home, null, T0, Window);
		await Connections.OpenAsync(second, Work, null, T0, Window);

		await Connections.CloseAsync(first, T0, T0.AddMinutes(1), SessionEndReason.LoggedOut);

		Assert.NotNull(Assert.Single((await ListAsync(first)).Items).Value.ClosedAt);
		Assert.Null(Assert.Single((await ListAsync(second)).Items).Value.ClosedAt);
	}

	[Fact]
	public async Task Activity_OpenConnectionOnly()
	{
		var open = await SessionAsync("open_user");
		var closed = await SessionAsync("closed_user", 2);
		await Connections.OpenAsync(open, Home, null, T0, Window);
		await Connections.OpenAsync(closed, Home, null, T0, Window);
		await Connections.CloseAsync(closed, T0, T0.AddMinutes(1), SessionEndReason.LoggedOut);

		await Connections.WriteActivityAsync(
			[new ConnectionActivity(open, T0.AddMinutes(9)), new ConnectionActivity(closed, T0.AddMinutes(9))]);

		Assert.Equal(T0.AddMinutes(9), Assert.Single((await ListAsync(open)).Items).Value.LastActiveAt);
		Assert.Equal(T0, Assert.Single((await ListAsync(closed)).Items).Value.LastActiveAt);
	}

	[Fact]
	public async Task NoActivity_NothingChanged()
	{
		var session = await SessionAsync();
		await Connections.OpenAsync(session, Home, null, T0, Window);

		await Connections.WriteActivityAsync([]);

		Assert.Equal(T0, Assert.Single((await ListAsync(session)).Items).Value.LastActiveAt);
	}

	[Fact]
	public async Task ClosingOpen_AtLastActivity()
	{
		var open = await SessionAsync("open_user");
		var done = await SessionAsync("done_user", 2);
		await Connections.OpenAsync(open, Home, null, T0, Window);
		await Connections.WriteActivityAsync([new ConnectionActivity(open, T0.AddMinutes(8))]);
		await Connections.OpenAsync(done, Home, null, T0, Window);
		await Connections.CloseAsync(done, T0, T0.AddMinutes(1), SessionEndReason.LoggedOut);

		var closed = await Connections.CloseOpenAsync(SessionEndReason.ServerStopped);

		Assert.Equal(1, closed);
		var stopped = Assert.Single((await ListAsync(open)).Items);
		Assert.Equal(T0.AddMinutes(8), stopped.Value.ClosedAt);
		Assert.Equal(SessionEndReason.ServerStopped, stopped.Value.CloseReason);
		Assert.Equal(SessionEndReason.LoggedOut, Assert.Single((await ListAsync(done)).Items).Value.CloseReason);
	}

	[Fact]
	public async Task SessionDelete_TrailDeleted()
	{
		var session = await SessionAsync();
		var user = (await Sessions.GetAsync((await Get<IUserRepository>().GetByNameAsync("trail_user", Cancel))!,
			session,
			Cancel))!.Value.User;
		await Connections.OpenAsync(session, Home, null, T0, Window);
		await Sessions.EndAsync(user, session, null, [SessionType.Api], T0.AddMinutes(1), SessionEndReason.Revoked);

		var deleted = await Sessions.DeleteEndedBeforeAsync(T0.AddDays(1));

		Assert.Equal(1, deleted);
		Assert.Empty((await ListAsync(session)).Items);
	}

	[Fact]
	public async Task MissingSession_ConnectionRefused()
	{
		await Assert.ThrowsAnyAsync<Exception>(() => Connections.OpenAsync(404, Home, null, T0, Window));
	}

	[Fact]
	public async Task SomeKindsEnding_AtLastActivity()
	{
		var stable = await SessionAsync("stable_user", 1, SessionType.Stable);
		var api = await SessionAsync("api_user", 2);
		await Sessions.WriteActivityAsync([new SessionActivity(stable, T0.AddMinutes(6), null)]);

		var ended = await Sessions.EndOpenAsync([SessionType.Stable, SessionType.StableTourney],
			SessionEndReason.ServerStopped);

		Assert.Equal(1, ended);
		var user = (await Get<IUserRepository>().GetByNameAsync("stable_user", Cancel))!;
		var end = (await Sessions.GetAsync(user, stable, Cancel))!.Value.End;
		Assert.Equal(new SessionEnd(T0.AddMinutes(6), SessionEndReason.ServerStopped), end);
		var apiUser = (await Get<IUserRepository>().GetByNameAsync("api_user", Cancel))!;
		Assert.Null((await Sessions.GetAsync(apiUser, api, Cancel))!.Value.End);
	}

	private static OsuClient Client()
	{
		return new OsuClient(
			new OsuVersion(new DateOnly(2026, 1, 2), 42, OsuVersionStream.Beta),
			new OsuFingerprint(
				new Md5("0cc175b9c0f1b6a831c399e269772661"),
				new Md5("4a8a08f09d37b73795649038408b5f33"),
				new Md5("f97c5d29941bfb1b2fdab0874906ab82"), new NetworkAdapters
				{
					Adapters = "adapter-one.adapter-two.",
					Hash = new Md5("92eb5ffee6ae2fec3ad71c777531578f")
				}));
	}
}
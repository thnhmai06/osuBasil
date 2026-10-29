using System.Net;
using Basil.Application.Auth.Persistence;
using Basil.Application.Common;
using Basil.Application.Users.Persistence;
using Basil.Domain.Auth;
using Basil.Domain.Bancho;
using Basil.Domain.Common;
using Basil.Domain.Users;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Auth.Tests.Persistence;

/// <summary>Contract tests of storing and listing account sessions.</summary>
[Collection("Persistence")]
public sealed class PostgresSessionRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly DateTimeOffset First = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Second = new(2026, 10, 22, 13, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Third = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);
	private static readonly Guid FirstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
	private static readonly Guid SecondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
	private static readonly Guid ThirdId = Guid.Parse("00000000-0000-0000-0000-000000000003");

	private static readonly SessionFilter All = new(SessionState.Any, null, Third);

	private ISessionRepository Sessions => Get<ISessionRepository>();

	[Fact]
	public async Task A_recorded_session_is_stored()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "login_user" }, TestContext.Current.CancellationToken);

		await Sessions.RecordAsync(new Session
		{
			Id = FirstId,
			User = user,
			Type = ConnectionType.Osu,
			Ip = IPAddress.Parse("192.0.2.10"),
			Client = Client(),
			StartedAt = First
		}, TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select id::text, user_id, type, host(ip) as ip, client_info, started_at, last_active_at, ended_at, " +
			"end_reason, token_hash, expires_at, must_reauthenticate, client_date, client_revision, client_stream, " +
			"osu_path_hash, network_adapters, network_adapters_hash, uninstall_hash, disk_signature_hash " +
			"from sessions",
			null, rows => rows.Count == 1, "the recorded session", TestContext.Current.CancellationToken);

		var row = rows[0];
		Assert.Equal(FirstId.ToString(), row.Text("id"));
		Assert.Equal(user.Id, row.Number("user_id"));
		Assert.Equal((int)ConnectionType.Osu, row.Number("type"));
		Assert.Equal("192.0.2.10", row.Text("ip"));
		Assert.Null(row["client_info"]);
		Assert.Equal(First, row.Instant("started_at"));
		Assert.Equal(First, row.Instant("last_active_at"));
		Assert.Null(row["ended_at"]);
		Assert.Null(row["end_reason"]);
		Assert.Null(row["token_hash"]);
		Assert.Null(row["expires_at"]);
		Assert.False(row.Bool("must_reauthenticate"));
		Assert.Equal("2026-01-02", row.Text("client_date"));
		Assert.Equal(42, row.Number("client_revision"));
		Assert.Equal((int)BanchoVersionStream.Beta, row.Number("client_stream"));
		Assert.Equal("0cc175b9c0f1b6a831c399e269772661", row.Text("osu_path_hash"));
		Assert.Equal("adapter-one.adapter-two.", row.Text("network_adapters"));
		Assert.Equal("92eb5ffee6ae2fec3ad71c777531578f", row.Text("network_adapters_hash"));
		Assert.Equal("4a8a08f09d37b73795649038408b5f33", row.Text("uninstall_hash"));
		Assert.Equal("f97c5d29941bfb1b2fdab0874906ab82", row.Text("disk_signature_hash"));
	}

	[Fact]
	public async Task A_session_without_an_osu_client_is_stored_without_client_data()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "login_user" }, TestContext.Current.CancellationToken);

		await Sessions.RecordAsync(new Session
		{
			Id = FirstId,
			User = user,
			Type = ConnectionType.Api,
			Ip = IPAddress.Parse("192.0.2.11"),
			StartedAt = First
		}, TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select client_date, client_revision, client_stream, osu_path_hash, network_adapters, " +
			"network_adapters_hash, uninstall_hash, disk_signature_hash from sessions",
			null, rows => rows.Count == 1, "the recorded session", TestContext.Current.CancellationToken);

		var row = rows[0];
		Assert.Null(row["client_date"]);
		Assert.Null(row["client_revision"]);
		Assert.Null(row["client_stream"]);
		Assert.Null(row["osu_path_hash"]);
		Assert.Null(row["network_adapters"]);
		Assert.Null(row["network_adapters_hash"]);
		Assert.Null(row["uninstall_hash"]);
		Assert.Null(row["disk_signature_hash"]);
	}

	[Fact]
	public async Task A_session_list_is_ordered_by_start_time_newest_first()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "login_user" }, TestContext.Current.CancellationToken);
		await RecordAsync(user, First, FirstId);
		await RecordAsync(user, Second, SecondId);
		await RecordAsync(user, Third, ThirdId);
		await WaitForSessionsAsync(3);

		var page = await Sessions.ListAsync(user, All, new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(3, page.Total);
		Assert.Equal(ThirdId, page.Items[0].Id);
		Assert.Equal(ConnectionType.Osu, page.Items[0].Type);
		Assert.Equal(Third, page.Items[0].StartedAt);
		Assert.Equal(Second, page.Items[1].StartedAt);
		Assert.Equal(First, page.Items[2].StartedAt);
	}

	[Fact]
	public async Task A_session_list_returns_the_requested_page()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "login_user" }, TestContext.Current.CancellationToken);
		await RecordAsync(user, First, FirstId);
		await RecordAsync(user, Second, SecondId);
		await RecordAsync(user, Third, ThirdId);
		await WaitForSessionsAsync(3);

		var page = await Sessions.ListAsync(user, All, new PageRequest(1, 1), TestContext.Current.CancellationToken);

		Assert.Equal(3, page.Total);
		Assert.Equal(Second, Assert.Single(page.Items).StartedAt);
	}

	[Fact]
	public async Task An_ended_session_persists_its_last_activity_and_end_reason()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "closed_session" }, TestContext.Current.CancellationToken);
		var session = new Session
		{
			Id = FirstId,
			User = user,
			Type = ConnectionType.Api,
			Ip = IPAddress.Parse("192.0.2.12"),
			StartedAt = First
		};
		await Sessions.RecordAsync(session, TestContext.Current.CancellationToken);
		session.MarkActive(Second);
		session.End(Third, SessionEndReason.Revoked);
		Assert.Equal(Second, session.LastActiveAt);
		Assert.Equal(Third, session.EndedAt);
		await Sessions.UpdateAsync(session);

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select last_active_at, ended_at, end_reason from sessions where id = @id",
			new Dictionary<string, object?> { ["id"] = FirstId },
			stored => stored.Count == 1,
			"the ended session", TestContext.Current.CancellationToken);

		Assert.Equal(Second, rows[0].Instant("last_active_at"));
		Assert.Equal(Third, rows[0].Instant("ended_at"));
		Assert.Equal((long)(byte)SessionEndReason.Revoked, rows[0].Number("end_reason"));
	}

	private static readonly byte[] Hash = System.Security.Cryptography.SHA256.HashData("token"u8);

	private async Task<UserRecord> UserAsync(string name = "token_user")
	{
		return await Get<IUserRepository>().CreateAsync(new UserData { Name = name }, TestContext.Current.CancellationToken);
	}

	private Task RecordTokenAsync(UserRecord user, Guid id, ConnectionType type, byte[]? hash,
		DateTimeOffset? expiresAt = null)
	{
		return Sessions.RecordAsync(new Session
		{
			Id = id,
			User = user,
			Type = type,
			Ip = IPAddress.Parse("192.0.2.20"),
			ClientInfo = "label",
			StartedAt = First,
			TokenHash = hash,
			ExpiresAt = expiresAt
		}, TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task A_token_finds_its_active_unexpired_session_of_its_kind()
	{
		var user = await UserAsync();
		await RecordTokenAsync(user, FirstId, ConnectionType.Api, Hash, Third);

		var found = await Sessions.FindByTokenAsync(ConnectionType.Api, Hash, Second, TestContext.Current.CancellationToken);

		Assert.NotNull(found);
		Assert.Equal(FirstId, found.Id);
		Assert.Equal(user.Id, found.UserId);
		Assert.Equal("label", found.ClientInfo);
		Assert.Equal(Third, found.ExpiresAt);
	}

	[Fact]
	public async Task A_token_finds_nothing_once_it_expired_ended_or_is_of_another_kind_or_hash()
	{
		var user = await UserAsync();
		await RecordTokenAsync(user, FirstId, ConnectionType.Api, Hash, Second);
		var cancel = TestContext.Current.CancellationToken;

		Assert.Null(await Sessions.FindByTokenAsync(ConnectionType.Api, Hash, Second, cancel));
		Assert.Null(await Sessions.FindByTokenAsync(ConnectionType.Irc, Hash, First, cancel));
		Assert.Null(await Sessions.FindByTokenAsync(ConnectionType.Api, [1, 2, 3], First, cancel));

		await Sessions.EndAsync(user, null, null, [ConnectionType.Api], Second, SessionEndReason.Revoked, false, cancel);
		Assert.Null(await Sessions.FindByTokenAsync(ConnectionType.Api, Hash, First, cancel));
	}

	[Fact]
	public async Task A_key_without_an_expiry_never_expires()
	{
		var user = await UserAsync();
		await RecordTokenAsync(user, FirstId, ConnectionType.Irc, Hash);

		Assert.NotNull(await Sessions.FindByTokenAsync(ConnectionType.Irc, Hash, First.AddYears(5),
			TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Ending_sessions_ends_the_unended_ones_of_the_user_and_spares_the_one_kept()
	{
		var user = await UserAsync();
		var other = await UserAsync("other_user");
		await RecordTokenAsync(user, FirstId, ConnectionType.Api, Hash);
		await RecordTokenAsync(user, SecondId, ConnectionType.Irc, SHA([2]));
		await RecordTokenAsync(user, ThirdId, ConnectionType.Api, SHA([3]));
		await RecordTokenAsync(other, Guid.Parse("00000000-0000-0000-0000-000000000004"), ConnectionType.Api, SHA([4]));
		var cancel = TestContext.Current.CancellationToken;

		await Sessions.EndAsync(user, null, FirstId, [ConnectionType.Api, ConnectionType.Irc], Third,
			SessionEndReason.Revoked, false, cancel);

		var page = await Sessions.ListAsync(user, All, new PageRequest(0, 10), cancel);
		Assert.Null(page.Items.Single(entry => entry.Id == FirstId).EndedAt);
		Assert.All(page.Items.Where(entry => entry.Id != FirstId), entry =>
		{
			Assert.Equal(Third, entry.EndedAt);
			Assert.Equal(SessionEndReason.Revoked, entry.EndReason);
		});
		var theirs = await Sessions.ListAsync(other, All, new PageRequest(0, 10), cancel);
		Assert.Null(Assert.Single(theirs.Items).EndedAt);
	}

	[Fact]
	public async Task Ending_one_session_leaves_the_others()
	{
		var user = await UserAsync();
		await RecordTokenAsync(user, FirstId, ConnectionType.Api, Hash);
		await RecordTokenAsync(user, SecondId, ConnectionType.Api, SHA([2]));
		var cancel = TestContext.Current.CancellationToken;

		await Sessions.EndAsync(user, SecondId, null, [ConnectionType.Api], Third, SessionEndReason.Revoked, false, cancel);

		Assert.Null((await Sessions.GetAsync(user, FirstId, cancel))!.EndedAt);
		Assert.Equal(Third, (await Sessions.GetAsync(user, SecondId, cancel))!.EndedAt);
	}

	[Fact]
	public async Task A_session_of_another_user_is_not_found()
	{
		var user = await UserAsync();
		var other = await UserAsync("other_user");
		await RecordTokenAsync(user, FirstId, ConnectionType.Api, Hash);

		Assert.Null(await Sessions.GetAsync(other, FirstId, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Ending_osu_sessions_marks_their_devices_even_when_they_had_ended_but_never_an_api_session()
	{
		var user = await UserAsync();
		var cancel = TestContext.Current.CancellationToken;
		await RecordAsync(user, First, FirstId);
		await RecordTokenAsync(user, SecondId, ConnectionType.Api, Hash);

		await Sessions.EndAsync(user, null, null, [ConnectionType.Osu, ConnectionType.Api], Third,
			SessionEndReason.Revoked, true, cancel);

		var osu = (await Sessions.GetAsync(user, FirstId, cancel))!;
		var api = (await Sessions.GetAsync(user, SecondId, cancel))!;
		Assert.True(osu.MustReauthenticate);
		Assert.False(api.MustReauthenticate);
		Assert.Equal(Third, api.EndedAt);
	}

	[Fact]
	public async Task Ending_sessions_without_the_device_mark_leaves_osu_devices_alone()
	{
		var user = await UserAsync();
		var cancel = TestContext.Current.CancellationToken;
		await RecordAsync(user, First, FirstId);

		await Sessions.EndAsync(user, null, null, [ConnectionType.Osu], Third, SessionEndReason.CredentialsChanged,
			false, cancel);

		Assert.False((await Sessions.GetAsync(user, FirstId, cancel))!.MustReauthenticate);
	}

	[Fact]
	public async Task A_marked_device_is_taken_once_and_only_by_its_own_hashes()
	{
		var user = await UserAsync();
		var cancel = TestContext.Current.CancellationToken;
		await RecordAsync(user, First, FirstId);
		await Sessions.EndAsync(user, null, null, [ConnectionType.Osu], Third, SessionEndReason.Revoked, true, cancel);
		var device = new DeviceId("4a8a08f09d37b73795649038408b5f33", "f97c5d29941bfb1b2fdab0874906ab82");

		Assert.False(await Sessions.TakeReauthenticationAsync(user, device with { UninstallHash = "different" }, cancel));
		Assert.True(await Sessions.TakeReauthenticationAsync(user, device, cancel));
		Assert.False(await Sessions.TakeReauthenticationAsync(user, device, cancel));
	}

	[Fact]
	public async Task Activity_is_stored_for_unended_sessions()
	{
		var user = await UserAsync();
		var cancel = TestContext.Current.CancellationToken;
		await RecordTokenAsync(user, FirstId, ConnectionType.Api, Hash, Second);

		await Sessions.WriteActivityAsync([new SessionActivity(FirstId, Third, Third.AddDays(7))], cancel);

		var entry = (await Sessions.GetAsync(user, FirstId, cancel))!;
		Assert.Equal(Third, entry.LastActiveAt);
		var found = await Sessions.FindByTokenAsync(ConnectionType.Api, Hash, Third.AddDays(6), cancel);
		Assert.Equal(Third.AddDays(7), found!.ExpiresAt);
	}

	[Fact]
	public async Task Deleting_ended_sessions_keeps_active_ones_and_the_marks_of_devices_that_must_reauthenticate()
	{
		var user = await UserAsync();
		var cancel = TestContext.Current.CancellationToken;
		await RecordTokenAsync(user, FirstId, ConnectionType.Api, Hash);
		await RecordTokenAsync(user, SecondId, ConnectionType.Irc, SHA([2]));
		await RecordAsync(user, First, ThirdId);
		await Sessions.EndAsync(user, FirstId, null, [ConnectionType.Api], Second, SessionEndReason.LoggedOut, false, cancel);
		await Sessions.EndAsync(user, ThirdId, null, [ConnectionType.Osu], Second, SessionEndReason.Revoked, true, cancel);

		var deleted = await Sessions.DeleteEndedBeforeAsync(Third, cancel);

		Assert.Equal(1, deleted);
		Assert.Null(await Sessions.GetAsync(user, FirstId, cancel));
		Assert.NotNull(await Sessions.GetAsync(user, SecondId, cancel));
		Assert.NotNull(await Sessions.GetAsync(user, ThirdId, cancel));
	}

	[Fact]
	public async Task Deleting_ended_sessions_keeps_the_ones_that_ended_after_the_cutoff()
	{
		var user = await UserAsync();
		var cancel = TestContext.Current.CancellationToken;
		await RecordTokenAsync(user, FirstId, ConnectionType.Api, Hash);
		await Sessions.EndAsync(user, FirstId, null, [ConnectionType.Api], Third, SessionEndReason.LoggedOut, false, cancel);

		Assert.Equal(0, await Sessions.DeleteEndedBeforeAsync(Second, cancel));
		Assert.NotNull(await Sessions.GetAsync(user, FirstId, cancel));
	}

	[Fact]
	public async Task A_session_list_filters_by_state_and_kind()
	{
		var user = await UserAsync();
		var cancel = TestContext.Current.CancellationToken;
		await RecordTokenAsync(user, FirstId, ConnectionType.Api, Hash, Third);
		await RecordTokenAsync(user, SecondId, ConnectionType.Irc, SHA([2]));
		await RecordTokenAsync(user, ThirdId, ConnectionType.Api, SHA([3]), Second);
		await Sessions.EndAsync(user, SecondId, null, [ConnectionType.Irc], Second, SessionEndReason.Revoked, false, cancel);
		var now = Third.AddHours(-1);

		var active = await Sessions.ListAsync(user, new SessionFilter(SessionState.Active, null, now), new PageRequest(0, 10), cancel);
		var ended = await Sessions.ListAsync(user, new SessionFilter(SessionState.Ended, null, now), new PageRequest(0, 10), cancel);
		var apiOnly = await Sessions.ListAsync(user, new SessionFilter(SessionState.Any, [ConnectionType.Api], now), new PageRequest(0, 10), cancel);

		Assert.Equal([FirstId], active.Items.Select(entry => entry.Id));
		Assert.Equal([SecondId, ThirdId], ended.Items.Select(entry => entry.Id).Order());
		Assert.Equal(2, apiOnly.Total);
	}

	private static byte[] SHA(byte[] input)
	{
		return System.Security.Cryptography.SHA256.HashData(input);
	}

	/// <summary>Records a session of an account.</summary>
	/// <param name="user">The account that logged in.</param>
	/// <param name="timestamp">The moment of the login.</param>
	private async Task RecordAsync(UserRecord user, DateTimeOffset timestamp, Guid id)
	{
		await Sessions.RecordAsync(new Session
		{
			Id = id,
			User = user,
			Type = ConnectionType.Osu,
			Ip = IPAddress.Parse("192.0.2.10"),
			Client = Client(),
			StartedAt = timestamp
		}, TestContext.Current.CancellationToken);
	}

	/// <summary>Waits until the given number of sessions is stored.</summary>
	/// <param name="count">How many sessions to wait for.</param>
	private async Task WaitForSessionsAsync(int count)
	{
		await StoredRows.WaitForAsync(ConnectionString, "select id from sessions", null,
			rows => rows.Count == count, $"{count} recorded sessions", TestContext.Current.CancellationToken);
	}

	/// <summary>Builds the osu! client a login reports.</summary>
	private static BanchoClient Client()
	{
		return new BanchoClient(
			new BanchoVersion(new DateOnly(2026, 1, 2), 42, BanchoVersionStream.Beta),
			new BanchoFingerprint(
				new Md5("0cc175b9c0f1b6a831c399e269772661"),
				new NetworkAdapters
				{
					Adapters = "adapter-one.adapter-two.",
					Hash = new Md5("92eb5ffee6ae2fec3ad71c777531578f")
				},
				new Md5("4a8a08f09d37b73795649038408b5f33"),
				new Md5("f97c5d29941bfb1b2fdab0874906ab82")));
	}
}

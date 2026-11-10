using System.Net;
using System.Security.Cryptography;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Common;
using Basil.Application.Users.Persistence;
using Basil.Domain.Authentication;
using Basil.Domain.Client;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Authentication.Tests.Persistence;

/// <summary>Contract tests of storing and listing account sessions.</summary>
[Collection("Persistence")]
public sealed class EfSessionRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly DateTimeOffset First = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Second = new(2026, 10, 22, 13, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Third = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);
	private static readonly long FirstId = 1;
	private static readonly long SecondId = 2;
	private static readonly long ThirdId = 3;

	private static SessionQuery All(UserRecord user)
	{
		return new SessionQuery(user, SessionState.Any, null, Third);
	}

	private static readonly Sha256 Hash = new("token"u8.ToArray());

	private ISessionRepository Sessions => Get<ISessionRepository>();

	[Fact]
	public async Task A_recorded_session_is_stored()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "login_user" });

		await Sessions.CreateOrUpdateAsync(
			Record(FirstId, user, SessionType.Stable, First,
				new SessionFingerprint(IPAddress.Parse("192.0.2.10"), Client()), Hash));

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select id, user_id, type, ip as ip, client_info, started_at, last_active_at, ended_at, " +
			"end_reason, token_hash, expires_at, client_date, client_revision, client_stream, " +
			"osu_path_hash, network_adapters, network_adapters_hash, uninstall_hash, disk_signature_hash " +
			"from sessions",
			null, rows => rows.Count == 1, "the recorded session", TestContext.Current.CancellationToken);

		var row = rows[0];
		Assert.Equal(FirstId.ToString(), row["id"]!.ToString());
		Assert.Equal(user.Id, row.Number("user_id"));
		Assert.Equal((int)SessionType.Stable, row.Number("type"));
		Assert.Equal("192.0.2.10", row.Text("ip"));
		Assert.Null(row["client_info"]);
		Assert.Equal(First, row.Instant("started_at"));
		Assert.Equal(First, row.Instant("last_active_at"));
		Assert.Null(row["ended_at"]);
		Assert.Null(row["end_reason"]);
		Assert.NotNull(row["token_hash"]);
		Assert.Null(row["expires_at"]);
		Assert.Equal("2026-01-02", row.Text("client_date"));
		Assert.Equal(42, row.Number("client_revision"));
		Assert.Equal((int)OsuVersionStream.Beta, row.Number("client_stream"));
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
			.CreateAsync(new UserData { Name = "login_user" });

		await Sessions.CreateOrUpdateAsync(
			Record(FirstId, user, SessionType.Api, First, new SessionFingerprint(IPAddress.Parse("192.0.2.11")), Hash));

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
			.CreateAsync(new UserData { Name = "login_user" });
		await RecordAsync(user, First, FirstId);
		await RecordAsync(user, Second, SecondId);
		await RecordAsync(user, Third, ThirdId);
		await WaitForSessionsAsync(3);

		var page = await Sessions.ListAsync(All(user), new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.False(page.HasNext);
		Assert.Equal(ThirdId, page.Items[0].Id);
		Assert.Equal(SessionType.Stable, page.Items[0].Value.Type);
		Assert.Equal(Third, page.Items[0].Value.StartedAt);
		Assert.Equal(Second, page.Items[1].Value.StartedAt);
		Assert.Equal(First, page.Items[2].Value.StartedAt);
	}

	[Fact]
	public async Task A_session_list_returns_the_requested_page()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "login_user" });
		await RecordAsync(user, First, FirstId);
		await RecordAsync(user, Second, SecondId);
		await RecordAsync(user, Third, ThirdId);
		await WaitForSessionsAsync(3);

		var page = await Sessions.ListAsync(All(user), new PageRequest(1, 1), TestContext.Current.CancellationToken);

		Assert.True(page.HasNext);
		Assert.Equal(Second, Assert.Single(page.Items).Value.StartedAt);
	}

	[Fact]
	public async Task An_ended_session_persists_its_last_activity_and_end_reason()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "closed_session" });
		var session = Record(FirstId, user, SessionType.Api, First,
			new SessionFingerprint(IPAddress.Parse("192.0.2.12")), Hash);
		await Sessions.CreateOrUpdateAsync(session);
		var ended = new SessionRecord
		{
			Id = session.Id,
			Value = session.Value with { LastActiveAt = Second, End = new SessionEnd(Third, SessionEndReason.Revoked) }
		};
		await Sessions.CreateOrUpdateAsync(ended);

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select last_active_at, ended_at, end_reason from sessions where id = @id",
			new Dictionary<string, object?> { ["id"] = FirstId },
			stored => stored.Count == 1,
			"the ended session", TestContext.Current.CancellationToken);

		Assert.Equal(Second, rows[0].Instant("last_active_at"));
		Assert.Equal(Third, rows[0].Instant("ended_at"));
		Assert.Equal((byte)SessionEndReason.Revoked, rows[0].Number("end_reason"));
	}

	[Fact]
	public async Task A_stored_osu_session_is_read_back_with_its_client()
	{
		var user = await UserAsync();
		await RecordAsync(user, First, FirstId);
		await WaitForSessionsAsync(1);

		var session = await Sessions.GetAsync(user, FirstId, TestContext.Current.CancellationToken);

		var client = Assert.IsType<OsuClient>(session!.Value.Fingerprint.Client);
		Assert.Equal(Client().Version, client.Version);
		Assert.Equal(Client().Fingerprint.PathHash, client.Fingerprint.PathHash);
		Assert.Equal(Client().Fingerprint.UninstallHash, client.Fingerprint.UninstallHash);
		Assert.Equal(Client().Fingerprint.DiskSignatureHash, client.Fingerprint.DiskSignatureHash);
		Assert.Equal(Client().Fingerprint.NetworkAdapters.Adapters, client.Fingerprint.NetworkAdapters.Adapters);
		Assert.Equal(Client().Fingerprint.NetworkAdapters.Hash, client.Fingerprint.NetworkAdapters.Hash);
	}

	[Fact]
	public async Task A_stored_token_session_is_read_back_with_its_credential()
	{
		var user = await UserAsync();
		await RecordTokenAsync(user, FirstId, SessionType.Api, Hash, Third);
		await WaitForSessionsAsync(1);

		var session = await Sessions.GetAsync(user, FirstId, TestContext.Current.CancellationToken);

		Assert.Equal(Hash, session!.Value.TokenHash);
		Assert.Equal(Third, session.Value.ExpiresAt);
		Assert.Null(session.Value.Fingerprint.Client);
	}

	private async Task<UserRecord> UserAsync(string name = "token_user")
	{
		return await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = name });
	}

	private Task RecordTokenAsync(UserRecord user, long id, SessionType type, Sha256 hash,
		DateTimeOffset? expiresAt = null)
	{
		return Sessions.CreateOrUpdateAsync(
			Record(id, user, type, First, new SessionFingerprint(IPAddress.Parse("192.0.2.20"), ClientInfo: "label"),
				hash, expiresAt));
	}

	private static SessionRecord Record(long id, UserRecord user, SessionType type, DateTimeOffset startedAt,
		SessionFingerprint fingerprint, Sha256 hash, DateTimeOffset? expiresAt = null)
	{
		return new SessionRecord
		{
			Id = id, Value = new SessionData(user, type, startedAt, fingerprint, hash, expiresAt, startedAt)
		};
	}

	[Fact]
	public async Task A_token_hash_that_another_session_holds_is_refused()
	{
		var user = await UserAsync();
		await RecordTokenAsync(user, FirstId, SessionType.Api, Hash, Third);
		await WaitForSessionsAsync(1);

		await Assert.ThrowsAnyAsync<Exception>(() => RecordTokenAsync(user, SecondId, SessionType.Api, Hash, Third));

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select id from sessions", null,
			rows => rows.Count >= 1, "the first session", TestContext.Current.CancellationToken);
		Assert.Equal(FirstId.ToString(), Assert.Single(rows)["id"]!.ToString());
	}

	[Fact]
	public async Task A_token_finds_its_active_unexpired_session_of_its_kind()
	{
		var user = await UserAsync();
		await RecordTokenAsync(user, FirstId, SessionType.Api, Hash, Third);

		var found = await Sessions.FindByTokenAsync(SessionType.Api, Hash, Second,
			TestContext.Current.CancellationToken);

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
		await RecordTokenAsync(user, FirstId, SessionType.Api, Hash, Second);
		var cancel = TestContext.Current.CancellationToken;

		Assert.Null(await Sessions.FindByTokenAsync(SessionType.Api, Hash, Second, cancel));
		Assert.Null(await Sessions.FindByTokenAsync(SessionType.Irc, Hash, First, cancel));
		Assert.Null(await Sessions.FindByTokenAsync(SessionType.Api, Sha("unknown"u8.ToArray()), First, cancel));

		await Sessions.EndAsync(user, null, null, [SessionType.Api], Second, SessionEndReason.Revoked);
		Assert.Null(await Sessions.FindByTokenAsync(SessionType.Api, Hash, First, cancel));
	}

	[Fact]
	public async Task A_key_without_an_expiry_never_expires()
	{
		var user = await UserAsync();
		await RecordTokenAsync(user, FirstId, SessionType.Irc, Hash);

		Assert.NotNull(await Sessions.FindByTokenAsync(SessionType.Irc, Hash, First.AddYears(5),
			TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Ending_sessions_ends_the_unended_ones_of_the_user_and_spares_the_one_kept()
	{
		var user = await UserAsync();
		var other = await UserAsync("other_user");
		await RecordTokenAsync(user, FirstId, SessionType.Api, Hash);
		await RecordTokenAsync(user, SecondId, SessionType.Irc, Sha([2]));
		await RecordTokenAsync(user, ThirdId, SessionType.Api, Sha([3]));
		await RecordTokenAsync(other, 4, SessionType.Api, Sha([4]));
		var cancel = TestContext.Current.CancellationToken;

		await Sessions.EndAsync(user, null, FirstId, [SessionType.Api, SessionType.Irc], Third,
			SessionEndReason.Revoked);

		var page = await Sessions.ListAsync(All(user), new PageRequest(0, 10), cancel);
		Assert.Null(page.Items.Single(entry => entry.Id == FirstId).Value.End?.At);
		Assert.All(page.Items.Where(entry => entry.Id != FirstId), entry =>
		{
			Assert.Equal(Third, entry.Value.End?.At);
			Assert.Equal(SessionEndReason.Revoked, entry.Value.End?.Reason);
		});
		var theirs = await Sessions.ListAsync(All(other), new PageRequest(0, 10), cancel);
		Assert.Null(Assert.Single(theirs.Items).Value.End?.At);
	}

	[Fact]
	public async Task Ending_one_session_leaves_the_others()
	{
		var user = await UserAsync();
		await RecordTokenAsync(user, FirstId, SessionType.Api, Hash);
		await RecordTokenAsync(user, SecondId, SessionType.Api, Sha([2]));
		var cancel = TestContext.Current.CancellationToken;

		await Sessions.EndAsync(user, SecondId, null, [SessionType.Api], Third, SessionEndReason.Revoked);

		Assert.Null((await Sessions.GetAsync(user, FirstId, cancel))!.Value.End?.At);
		Assert.Equal(Third, (await Sessions.GetAsync(user, SecondId, cancel))!.Value.End?.At);
	}

	[Fact]
	public async Task A_session_of_another_user_is_not_found()
	{
		var user = await UserAsync();
		var other = await UserAsync("other_user");
		await RecordTokenAsync(user, FirstId, SessionType.Api, Hash);

		Assert.Null(await Sessions.GetAsync(other, FirstId, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Deleting_ended_sessions_keeps_the_active_ones()
	{
		var user = await UserAsync();
		var cancel = TestContext.Current.CancellationToken;
		await RecordTokenAsync(user, FirstId, SessionType.Api, Hash);
		await RecordTokenAsync(user, SecondId, SessionType.Irc, Sha([2]));
		await RecordAsync(user, First, ThirdId);
		await Sessions.EndAsync(user, FirstId, null, [SessionType.Api], Second, SessionEndReason.LoggedOut);
		await Sessions.EndAsync(user, ThirdId, null, [SessionType.Stable], Second, SessionEndReason.Revoked);

		var deleted = await Sessions.DeleteEndedBeforeAsync(Third);

		Assert.Equal(2, deleted);
		Assert.Null(await Sessions.GetAsync(user, FirstId, cancel));
		Assert.NotNull(await Sessions.GetAsync(user, SecondId, cancel));
		Assert.Null(await Sessions.GetAsync(user, ThirdId, cancel));
	}

	[Fact]
	public async Task Deleting_ended_sessions_keeps_the_ones_that_ended_after_the_cutoff()
	{
		var user = await UserAsync();
		var cancel = TestContext.Current.CancellationToken;
		await RecordTokenAsync(user, FirstId, SessionType.Api, Hash);
		await Sessions.EndAsync(user, FirstId, null, [SessionType.Api], Third, SessionEndReason.LoggedOut);

		Assert.Equal(0, await Sessions.DeleteEndedBeforeAsync(Second));
		Assert.NotNull(await Sessions.GetAsync(user, FirstId, cancel));
	}

	[Fact]
	public async Task A_session_list_filters_by_state_and_kind()
	{
		var user = await UserAsync();
		var cancel = TestContext.Current.CancellationToken;
		await RecordTokenAsync(user, FirstId, SessionType.Api, Hash, Third);
		await RecordTokenAsync(user, SecondId, SessionType.Irc, Sha([2]));
		await RecordTokenAsync(user, ThirdId, SessionType.Api, Sha([3]), Second);
		await Sessions.EndAsync(user, SecondId, null, [SessionType.Irc], Second, SessionEndReason.Revoked);
		var now = Third.AddHours(-1);

		var active = await Sessions.ListAsync(new SessionQuery(user, SessionState.Active, null, now),
			new PageRequest(0, 10), cancel);
		var ended = await Sessions.ListAsync(new SessionQuery(user, SessionState.Ended, null, now),
			new PageRequest(0, 10), cancel);
		var apiOnly = await Sessions.ListAsync(new SessionQuery(user, SessionState.Any, [SessionType.Api], now),
			new PageRequest(0, 10), cancel);

		Assert.Equal([FirstId], active.Items.Select(entry => entry.Id));
		Assert.Equal([SecondId, ThirdId], ended.Items.Select(entry => entry.Id).Order());
		Assert.Equal(2, apiOnly.Items.Count);
	}

	private static Sha256 Sha(byte[] input)
	{
		return new Sha256(input);
	}

	/// <summary>Records a session of an account.</summary>
	/// <param name="user">The account that logged in.</param>
	/// <param name="timestamp">The moment of the login.</param>
	private async Task RecordAsync(UserRecord user, DateTimeOffset timestamp, long id)
	{
		await Sessions.CreateOrUpdateAsync(
			Record(id, user, SessionType.Stable, timestamp,
				new SessionFingerprint(IPAddress.Parse("192.0.2.10"), Client()), Sha(BitConverter.GetBytes(id))));
	}

	/// <summary>Waits until the given number of sessions is stored.</summary>
	/// <param name="count">How many sessions to wait for.</param>
	[Fact]
	public async Task A_recorded_session_is_in_the_database_when_the_write_returns()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "login_user" });

		await RecordAsync(user, First, FirstId);

		var rows = await StoredRows.ReadAsync(ConnectionString, "select id from sessions", null,
			TestContext.Current.CancellationToken);
		Assert.Single(rows);
	}

	[Fact]
	public async Task Storing_a_session_twice_keeps_one_session_with_the_latest_state()
	{
		var user = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "login_user" });
		var cancel = TestContext.Current.CancellationToken;
		await RecordAsync(user, First, FirstId);
		var stored = (await Sessions.GetAsync(user, FirstId, cancel))!;

		await Sessions.CreateOrUpdateAsync(
			new SessionRecord { Id = stored.Id, Value = stored.Value with { LastActiveAt = Second } });

		var page = await Sessions.ListAsync(All(user), new PageRequest(0, 10), cancel);
		Assert.Equal(Second, Assert.Single(page.Items).Value.LastActiveAt);
	}

	[Fact]
	public async Task A_session_of_an_account_that_does_not_exist_is_refused_to_the_caller()
	{
		var ghost = new UserRecord { Id = 9999, Value = new UserData { Name = "ghost" } };

		await Assert.ThrowsAnyAsync<Exception>(() => RecordAsync(ghost, First, FirstId));
	}

	private async Task WaitForSessionsAsync(int count)
	{
		await StoredRows.WaitForAsync(ConnectionString, "select id from sessions", null,
			rows => rows.Count == count, $"{count} recorded sessions", TestContext.Current.CancellationToken);
	}

	/// <summary>Builds the osu! client a login reports.</summary>
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
using System.Net;
using Basil.Application.Persistence.Auth;
using Basil.Application.Persistence.Common;
using Basil.Application.Persistence.Users;
using Basil.Domain.Auth;
using Basil.Domain.Bancho;
using Basil.Domain.Common;
using Basil.Domain.Users;
using Basil.Infrastructure.Persistence.Tests.Support;

namespace Basil.Infrastructure.Persistence.Tests.Auth;

/// <summary>Contract tests of the recording of the login history of an account.</summary>
[Collection("Persistence")]
public sealed class PostgresLoginRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly DateTimeOffset First = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Second = new(2026, 10, 22, 13, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Third = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);

	private ILoginRepository Logins => Get<ILoginRepository>();

	[Fact]
	public async Task A_recorded_login_is_stored()
	{
		var user = await Get<IUserRepository>().CreateAsync(new UserData { Name = "login_user" }, TestContext.Current.CancellationToken);

		await Logins.RecordAsync(new Login
		{
			User = user,
			Ip = IPAddress.Parse("192.0.2.10"),
			Client = Client(),
			Timestamp = First
		}, TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select user_id, host(ip) as ip, timestamp, client_date, client_revision, client_stream, " +
			"osu_path_hash, network_adapters, network_adapters_hash, uninstall_hash, disk_signature_hash " +
			"from logins",
			null, rows => rows.Count == 1, "the recorded login", TestContext.Current.CancellationToken);

		var row = rows[0];
		Assert.Equal(user.Id, row.Number("user_id"));
		Assert.Equal("192.0.2.10", row.Text("ip"));
		Assert.Equal(First, row.Instant("timestamp"));
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
	public async Task A_login_without_a_client_is_stored_without_client_data()
	{
		var user = await Get<IUserRepository>().CreateAsync(new UserData { Name = "login_user" }, TestContext.Current.CancellationToken);

		await Logins.RecordAsync(new Login
		{
			User = user,
			Ip = IPAddress.Parse("192.0.2.11"),
			Timestamp = First
		}, TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select client_date, client_revision, client_stream, osu_path_hash, network_adapters, " +
			"network_adapters_hash, uninstall_hash, disk_signature_hash from logins",
			null, rows => rows.Count == 1, "the recorded login", TestContext.Current.CancellationToken);

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
	public async Task A_login_history_is_listed_newest_first()
	{
		var user = await Get<IUserRepository>().CreateAsync(new UserData { Name = "login_user" }, TestContext.Current.CancellationToken);
		await RecordAsync(user, First);
		await RecordAsync(user, Second);
		await RecordAsync(user, Third);
		await WaitForLoginsAsync(3);

		var page = await Logins.ListAsync(user, new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(3, page.Total);
		Assert.Equal(Third, page.Items[0].Timestamp);
		Assert.Equal(Second, page.Items[1].Timestamp);
		Assert.Equal(First, page.Items[2].Timestamp);
	}

	[Fact]
	public async Task A_login_history_returns_the_requested_part()
	{
		var user = await Get<IUserRepository>().CreateAsync(new UserData { Name = "login_user" }, TestContext.Current.CancellationToken);
		await RecordAsync(user, First);
		await RecordAsync(user, Second);
		await RecordAsync(user, Third);
		await WaitForLoginsAsync(3);

		var page = await Logins.ListAsync(user, new PageRequest(1, 1), TestContext.Current.CancellationToken);

		Assert.Equal(3, page.Total);
		Assert.Equal(Second, Assert.Single(page.Items).Timestamp);
	}

	/// <summary>Records a login of an account.</summary>
	/// <param name="user">The account that logged in.</param>
	/// <param name="timestamp">The moment of the login.</param>
	private async Task RecordAsync(UserRecord user, DateTimeOffset timestamp)
	{
		await Logins.RecordAsync(new Login
		{
			User = user,
			Ip = IPAddress.Parse("192.0.2.10"),
			Client = Client(),
			Timestamp = timestamp
		}, TestContext.Current.CancellationToken);
	}

	/// <summary>Waits until the given number of logins is stored.</summary>
	/// <param name="count">How many logins to wait for.</param>
	private async Task WaitForLoginsAsync(int count)
	{
		await StoredRows.WaitForAsync(ConnectionString, "select id from logins", null,
			rows => rows.Count == count, $"{count} recorded logins", TestContext.Current.CancellationToken);
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

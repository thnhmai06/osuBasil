using System.Text;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Cli.Tests.Commands;

/// <summary>The <c>users</c> commands: what they print and what they change in the server.</summary>
[Collection("CLI host")]
public sealed class UserCommandTests(CliFixture fixture)
{
	private async Task<bool> CanSignInAsync(string name, string password)
	{
		var credentials = fixture.Host.Services.GetRequiredService<ICredentialRepository>();
		return await credentials.VerifyAsync(new Credentials(await fixture.UserAsync(name),
			new Md5(Encoding.UTF8.GetBytes(password))));
	}

	[Fact]
	public async Task Create_DefaultsAndPassword()
	{
		var name = CliFixture.Name("create");

		var (code, text) = await fixture.RunAsync($"users create --username {name} --password pw1");

		var user = await fixture.UserAsync(name);
		Assert.Equal(0, code);
		Assert.Equal($"Created {name} with id {user.Id}.", text);
		Assert.Equal(UserPrivilegesExtensions.Default, user.Value.Privileges);
		Assert.True(await CanSignInAsync(name, "pw1"));
	}

	[Fact]
	public async Task CreateAdminMask_AppliesMasks()
	{
		var admin = CliFixture.Name("admin");
		var mask = CliFixture.Name("mask");

		await fixture.RunAsync($"users create --username {admin} --password pw --admin");
		await fixture.RunAsync(
			$"users create --username {mask} --password pw --privileges {(ulong)UserPrivileges.Player}");

		Assert.Equal(UserPrivilegesExtensions.All, (await fixture.UserAsync(admin)).Value.Privileges);
		Assert.Equal(UserPrivileges.Player, (await fixture.UserAsync(mask)).Value.Privileges);
	}

	[Fact]
	public async Task CreateTakenName_Refused()
	{
		var name = CliFixture.Name("taken");
		await fixture.RunAsync($"users create --username {name} --password pw");

		var (code, text) = await fixture.RunAsync($"users create --username {name} --password pw");

		Assert.Equal(1, code);
		Assert.Equal("error: nameTaken: Name taken.", text);
	}

	[Fact]
	public async Task CreateNoPassword_UsageError()
	{
		var (code, text) = await fixture.RunAsync("users create --username nopass");

		Assert.Equal(2, code);
		Assert.Contains("--password is required", text);
	}

	[Fact]
	public async Task Show_PrintsPrivilegeNames()
	{
		var name = CliFixture.Name("show");
		await fixture.RunAsync(
			$"users create --username {name} --password pw --privileges {(ulong)(UserPrivileges.Player | UserPrivileges.Supporter)}");
		var user = await fixture.UserAsync(name);

		var (code, text) = await fixture.RunAsync($"users {user.Id}");

		Assert.Equal(0, code);
		Assert.Contains("name:", text);
		Assert.Contains(name, text);
		Assert.Contains("(Player, Supporter)", text);
	}

	[Fact]
	public async Task ShowUnknownAccount_NotFound()
	{
		var (code, text) = await fixture.RunAsync("users 999999");

		Assert.Equal(1, code);
		Assert.Equal("error: notFound: No such user.", text);
	}

	[Fact]
	public async Task ListSearch_PrintsPositionedTable()
	{
		var name = CliFixture.Name("listed");
		await fixture.RunAsync($"users create --username {name} --password pw");

		var (code, text) = await fixture.RunAsync($"users list --query {name} --limit 5");

		Assert.Equal(0, code);
		Assert.Contains(name, text);
		Assert.Contains("shown: 1 (offset 0, limit 5)", text);
	}

	[Fact]
	public async Task ListLimitOver100_Invalid()
	{
		var (code, _) = await fixture.RunAsync("users list --limit 101");

		Assert.Equal(2, code);
	}

	[Fact]
	public async Task Set_OnlyGivenFields()
	{
		var name = CliFixture.Name("set");
		var renamed = name + "x";
		await fixture.RunAsync($"users create --username {name} --password pw");
		var user = await fixture.UserAsync(name);

		var (code, _) =
			await fixture.RunAsync(
				$"users {user.Id} set --username {renamed} --country vn --privileges {(ulong)UserPrivileges.Player}");

		Assert.Equal(0, code);
		var changed = await fixture.UserAsync(renamed);
		Assert.Equal(user.Id, changed.Id);
		Assert.Equal(Country.Vn, changed.Value.Country);
		Assert.Equal(UserPrivileges.Player, changed.Value.Privileges);
		Assert.True(await CanSignInAsync(renamed, "pw"));
	}

	[Fact]
	public async Task SetPassword_Replaced()
	{
		var name = CliFixture.Name("pass");
		await fixture.RunAsync($"users create --username {name} --password old");
		var user = await fixture.UserAsync(name);

		var (code, _) = await fixture.RunAsync($"users {user.Id} set --password new");

		Assert.Equal(0, code);
		Assert.True(await CanSignInAsync(name, "new"));
		Assert.False(await CanSignInAsync(name, "old"));
	}

	[Fact]
	public async Task SetAwayPm_Applied()
	{
		var name = CliFixture.Name("away");
		await fixture.Host.CreateUserAsync(name);
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);
		await client.LoginAsync(name);
		var user = await fixture.UserAsync(name);

		var (code, _) = await fixture.RunAsync($"users {user.Id} set --away brb --pmPrivate");

		var online = fixture.Host.Services.GetRequiredService<UserRegistry>().Of(user);
		Assert.Equal(0, code);
		Assert.Equal("brb", online.AwayMessage);
		Assert.True(online.PmPrivate);
	}

	[Fact]
	public async Task SetWrongCountry_Invalid()
	{
		var name = CliFixture.Name("country");
		await fixture.RunAsync($"users create --username {name} --password pw");
		var user = await fixture.UserAsync(name);

		var (code, _) = await fixture.RunAsync($"users {user.Id} set --country zzz");

		Assert.Equal(2, code);
	}

	[Fact]
	public async Task BotPrivileges_SystemRefused()
	{
		var (code, text) =
			await fixture.RunAsync($"users {BasilBot.Id} set --privileges {(ulong)UserPrivilegesExtensions.All}");

		Assert.Equal(1, code);
		Assert.Contains("systemAccount", text);
	}

	[Fact]
	public async Task AdminSet_ConsoleAllowed()
	{
		var name = CliFixture.Name("rank");
		await fixture.RunAsync($"users create --username {name} --password pw --admin");
		var user = await fixture.UserAsync(name);

		var (code, _) = await fixture.RunAsync($"users {user.Id} set --privileges {(ulong)UserPrivileges.None}");

		Assert.Equal(0, code);
		Assert.Equal(UserPrivileges.None, (await fixture.UserAsync(name)).Value.Privileges);
	}

	[Theory]
	[InlineData("friends")]
	[InlineData("blocks")]
	public async Task OtherRelationships_DeniedInConsole(string word)
	{
		var owner = await NewAsync($"{word[0]}o");
		var other = await NewAsync($"{word[0]}t");

		var (put, _) = await fixture.RunAsync($"users {owner.Id} {word} {other.Id} put");
		var (delete, _) = await fixture.RunAsync($"users {owner.Id} {word} {other.Id} delete");
		var (list, listed) = await fixture.RunAsync($"users {owner.Id} {word} list");

		Assert.NotEqual(0, put);
		Assert.NotEqual(0, delete);
		Assert.Equal(0, list);
		Assert.DoesNotContain(other.Value.Name, listed);
	}

	[Fact]
	public async Task SilenceAndUnsilence_SetsAndClears()
	{
		var user = await NewAsync("sl");

		var (silenced, _) = await fixture.RunAsync($"users {user.Id} silence --duration 90m --reason spam");
		var whileSilenced = await fixture.Host.Services.GetRequiredService<IUserRepository>()
			.GetAsync(user.Id, TestContext.Current.CancellationToken);
		var (unsilenced, _) = await fixture.RunAsync($"users {user.Id} unsilence");
		var afterwards = await fixture.Host.Services.GetRequiredService<IUserRepository>()
			.GetAsync(user.Id, TestContext.Current.CancellationToken);

		Assert.Equal(0, silenced);
		Assert.Equal(fixture.Host.Time.GetUtcNow() + TimeSpan.FromMinutes(90), whileSilenced!.Value.SilencedUntil);
		Assert.Equal(0, unsilenced);
		Assert.Null(afterwards!.Value.SilencedUntil);
	}

	[Fact]
	public async Task SilenceWrongDuration_Invalid()
	{
		var user = await NewAsync("sw");

		var (code, _) = await fixture.RunAsync($"users {user.Id} silence --duration soon");

		Assert.Equal(2, code);
	}

	[Fact]
	public async Task SessionCommands_ApiSessionWorks()
	{
		var name = CliFixture.Name("sess");
		await fixture.Host.CreateUserAsync(name);
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);
		var id = await client.LoginAsync(name);

		var (_, listed) =
			await fixture.RunAsync($"users {id} sessions list --state online --types {(int)SessionType.Api}");
		var sessionId = listed.Split('\n').Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
			.First(word => long.TryParse(word, out _));
		var (shown, shownText) = await fixture.RunAsync($"users {id} sessions {sessionId}");
		var (closed, _) = await fixture.RunAsync($"users {id} sessions {sessionId} delete");
		var (status, _) = await client.CallAsync(HttpMethod.Get, "/v1/auth/session");

		Assert.Contains("api", listed);
		Assert.Equal(0, shown);
		Assert.Contains("startedAt", shownText);
		Assert.Equal(0, closed);
		Assert.Equal(401, status);
	}

	[Fact]
	public async Task SessionConnections_TrailListed()
	{
		var name = CliFixture.Name("trail");
		await fixture.Host.CreateUserAsync(name);
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);
		var id = await client.LoginAsync(name);
		var (_, listed) =
			await fixture.RunAsync($"users {id} sessions list --state online --types {(int)SessionType.Api}");
		var sessionId = listed.Split('\n').Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
			.First(word => long.TryParse(word, out _));

		var (code, text) = await fixture.RunAsync($"users {id} sessions {sessionId} connections");

		Assert.Equal(0, code);
		Assert.Contains("openedAt", text);
		Assert.Contains("shown: 1", text);
	}

	[Fact]
	public async Task DeleteAllSessions_AllClosed()
	{
		var name = CliFixture.Name("sessall");
		await fixture.Host.CreateUserAsync(name);
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);
		var id = await client.LoginAsync(name);

		var (code, _) = await fixture.RunAsync($"users {id} sessions delete");

		Assert.Equal(0, code);
		Assert.Equal(401, (await client.CallAsync(HttpMethod.Get, "/v1/auth/session")).Status);
	}

	[Fact]
	public async Task Delete_AccountGone()
	{
		var user = await NewAsync("del");

		var (code, _) = await fixture.RunAsync($"users {user.Id} delete");
		var (shown, text) = await fixture.RunAsync($"users {user.Id}");

		Assert.Equal(0, code);
		Assert.Equal(1, shown);
		Assert.Contains("notFound", text);
	}

	[Fact]
	public async Task AvatarUnknownAccount_NotFound()
	{
		var (code, _) = await fixture.RunAsync("users 999999 avatar delete");

		Assert.Equal(1, code);
	}

	private async Task<UserRecord> NewAsync(string prefix)
	{
		var name = CliFixture.Name(prefix);
		await fixture.RunAsync($"users create --username {name} --password pw");
		return await fixture.UserAsync(name);
	}
}
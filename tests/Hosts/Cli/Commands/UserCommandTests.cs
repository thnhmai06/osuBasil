using System.Text;
using Basil.Application.Auth.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Users;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Users;
using Basil.Host.Cli.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Cli.Tests.Commands;

/// <summary>The <c>users</c> commands: what they print and what they change in the server.</summary>
[Collection("CLI host")]
public sealed class UserCommandTests(CliFixture fixture)
{
	private async Task<bool> CanSignInAsync(string name, string password)
	{
		var credentials = fixture.Host.Services.GetRequiredService<ICredentialRepository>();
		return await credentials.VerifyAsync(new Credentials(await fixture.UserAsync(name), new Md5(Encoding.UTF8.GetBytes(password))));
	}

	[Fact]
	public async Task Create_StoresTheAccountWithTheDefaultPermissionsAndAPassword()
	{
		var name = CliFixture.Name("create");

		var (code, text) = await fixture.RunAsync($"users create --username {name} --password pw1");

		var user = await fixture.UserAsync(name);
		Assert.Equal(0, code);
		Assert.Equal($"Created {name} with id {user.Id}.", text);
		Assert.Equal(UserPermissionsGroups.Default, user.Value.Permissions);
		Assert.True(await CanSignInAsync(name, "pw1"));
	}

	[Fact]
	public async Task Create_WithAdmin_GivesEveryPermission_AndWithPermissions_GivesTheMask()
	{
		var admin = CliFixture.Name("admin");
		var mask = CliFixture.Name("mask");

		await fixture.RunAsync($"users create --username {admin} --password pw --admin");
		await fixture.RunAsync($"users create --username {mask} --password pw --permissions {(ulong)UserPermissions.ChatPost}");

		Assert.Equal(UserPermissionsGroups.All, (await fixture.UserAsync(admin)).Value.Permissions);
		Assert.Equal(UserPermissions.ChatPost, (await fixture.UserAsync(mask)).Value.Permissions);
	}

	[Fact]
	public async Task Create_ATakenName_IsRefusedWithCode1AndTheApiCode()
	{
		var name = CliFixture.Name("taken");
		await fixture.RunAsync($"users create --username {name} --password pw");

		var (code, text) = await fixture.RunAsync($"users create --username {name} --password pw");

		Assert.Equal(1, code);
		Assert.Equal("error: nameTaken: Name taken.", text);
	}

	[Fact]
	public async Task Create_WithoutAPassword_IsAUsageErrorWithCode2()
	{
		var (code, text) = await fixture.RunAsync("users create --username nopass");

		Assert.Equal(2, code);
		Assert.Contains("--password is required", text);
	}

	[Fact]
	public async Task Show_PrintsTheAccountWithTheNamesOfItsPermissions()
	{
		var name = CliFixture.Name("show");
		await fixture.RunAsync($"users create --username {name} --password pw --permissions {(ulong)(UserPermissions.ChatPost | UserPermissions.ChatPostPrivate)}");
		var user = await fixture.UserAsync(name);

		var (code, text) = await fixture.RunAsync($"users {user.Id}");

		Assert.Equal(0, code);
		Assert.Contains($"name:", text);
		Assert.Contains(name, text);
		Assert.Contains("(ChatPost, ChatPostPrivate)", text);
	}

	[Fact]
	public async Task Show_AnUnknownAccount_IsNotFound()
	{
		var (code, text) = await fixture.RunAsync("users 999999");

		Assert.Equal(1, code);
		Assert.Equal("error: notFound: No such user.", text);
	}

	[Fact]
	public async Task List_FiltersWithTheSearchSyntax_AndPrintsATableWithItsPosition()
	{
		var name = CliFixture.Name("listed");
		await fixture.RunAsync($"users create --username {name} --password pw");

		var (code, text) = await fixture.RunAsync($"users list --query {name} --limit 5");

		Assert.Equal(0, code);
		Assert.Contains(name, text);
		Assert.Contains("1 of 1 (offset 0, limit 5)", text);
	}

	[Fact]
	public async Task List_WithALimitOver100_IsInvalid()
	{
		var (code, _) = await fixture.RunAsync("users list --limit 101");

		Assert.Equal(2, code);
	}

	[Fact]
	public async Task Set_ChangesOnlyTheGivenFields()
	{
		var name = CliFixture.Name("set");
		var renamed = name + "x";
		await fixture.RunAsync($"users create --username {name} --password pw");
		var user = await fixture.UserAsync(name);

		var (code, _) = await fixture.RunAsync($"users {user.Id} set --username {renamed} --country vn --permissions {(ulong)UserPermissions.ChatPost}");

		Assert.Equal(0, code);
		var changed = await fixture.UserAsync(renamed);
		Assert.Equal(user.Id, changed.Id);
		Assert.Equal(Country.Vn, changed.Value.Country);
		Assert.Equal(UserPermissions.ChatPost, changed.Value.Permissions);
		Assert.True(await CanSignInAsync(renamed, "pw"));
	}

	[Fact]
	public async Task Set_Password_ReplacesThePassword()
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
	public async Task Set_AwayAndPmPrivate_AreSetForTheAccount()
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
	public async Task Set_AWrongCountry_IsInvalid()
	{
		var name = CliFixture.Name("country");
		await fixture.RunAsync($"users create --username {name} --password pw");
		var user = await fixture.UserAsync(name);

		var (code, _) = await fixture.RunAsync($"users {user.Id} set --country zzz");

		Assert.Equal(2, code);
	}

	[Fact]
	public async Task Set_PermissionsOnBasilBot_IsRefusedAsASystemAccount()
	{
		var (code, text) = await fixture.RunAsync($"users {BasilBot.Id} set --permissions {(ulong)UserPermissionsGroups.All}");

		Assert.Equal(1, code);
		Assert.Contains("systemAccount", text);
	}

	[Fact]
	public async Task Set_OnAnAdministrator_Works_BecauseTheConsoleOutranksEveryone()
	{
		var name = CliFixture.Name("rank");
		await fixture.RunAsync($"users create --username {name} --password pw --admin");
		var user = await fixture.UserAsync(name);

		var (code, _) = await fixture.RunAsync($"users {user.Id} set --permissions {(ulong)UserPermissions.None}");

		Assert.Equal(0, code);
		Assert.Equal(UserPermissions.None, user.Value.Permissions);
	}

	[Fact]
	public async Task Friends_PutListDelete_AreHeldByTheAccountNamedFirst()
	{
		var owner = await NewAsync("fo");
		var other = await NewAsync("ft");

		var (put, _) = await fixture.RunAsync($"users {owner.Id} friends {other.Id} put");
		var (_, listed) = await fixture.RunAsync($"users {owner.Id} friends list");
		var (otherList, _) = await fixture.RunAsync($"users {other.Id} friends list");
		var (delete, _) = await fixture.RunAsync($"users {owner.Id} friends {other.Id} delete");
		var (_, after) = await fixture.RunAsync($"users {owner.Id} friends list");

		Assert.Equal(0, put);
		Assert.Contains(other.Value.Name, listed);
		Assert.Equal(0, otherList);
		Assert.Equal(0, delete);
		Assert.DoesNotContain(other.Value.Name, after);
	}

	[Fact]
	public async Task Blocks_PutListDelete_Work()
	{
		var owner = await NewAsync("bo");
		var other = await NewAsync("bt");

		await fixture.RunAsync($"users {owner.Id} blocks {other.Id} put");
		var (_, listed) = await fixture.RunAsync($"users {owner.Id} blocks list");
		await fixture.RunAsync($"users {owner.Id} blocks {other.Id} delete");
		var (_, after) = await fixture.RunAsync($"users {owner.Id} blocks list");

		Assert.Contains(other.Value.Name, listed);
		Assert.DoesNotContain(other.Value.Name, after);
	}

	[Fact]
	public async Task Friends_OfTheAccountWithItself_IsRefused()
	{
		var owner = await NewAsync("fs");

		var (code, text) = await fixture.RunAsync($"users {owner.Id} friends {owner.Id} put");

		Assert.Equal(2, code);
		Assert.Contains("self", text);
	}

	[Fact]
	public async Task Restrictions_CreateListLiftDelete_WorkAndTheMaskIsNamed()
	{
		var user = await NewAsync("rs");
		var mask = (ulong)UserPermissions.ChatPost;

		var (created, createdText) = await fixture.RunAsync($"users {user.Id} restrictions create --permissions {mask} --duration 2h --reason spam");
		var (_, listed) = await fixture.RunAsync($"users {user.Id} restrictions list");
		var restrictions = await fixture.Host.Services.GetRequiredService<IRestrictionRepository>().ListAsync(user);
		var id = restrictions.Single().Id;
		var (lifted, _) = await fixture.RunAsync($"users {user.Id} restrictions {id} lift");
		var (deleted, _) = await fixture.RunAsync($"users {user.Id} restrictions {id} delete");

		Assert.Equal(0, created);
		Assert.Contains($"restriction {id}", createdText);
		Assert.Contains("permissions", listed);
		Assert.Equal(0, lifted);
		Assert.Equal(0, deleted);
		Assert.Empty(await fixture.Host.Services.GetRequiredService<IRestrictionRepository>().ListAsync(user));
	}

	[Fact]
	public async Task Restrictions_Duration_SetsTheEndFromTheServersClock()
	{
		var user = await NewAsync("rd");

		await fixture.RunAsync($"users {user.Id} restrictions create --permissions {(ulong)UserPermissions.ChatPost} --duration 90m");

		var restriction = (await fixture.Host.Services.GetRequiredService<IRestrictionRepository>().ListAsync(user)).Single();
		Assert.Equal(fixture.Host.Time.GetUtcNow() + TimeSpan.FromMinutes(90), restriction.Value.EndsAt);
	}

	[Fact]
	public async Task Restrictions_AWrongDuration_IsInvalid()
	{
		var user = await NewAsync("rw");

		var (code, _) = await fixture.RunAsync($"users {user.Id} restrictions create --permissions 1 --duration soon");

		Assert.Equal(2, code);
	}

	[Fact]
	public async Task Sessions_ListShowAndClose_WorkForAnOnlineApiSession()
	{
		var name = CliFixture.Name("sess");
		await fixture.Host.CreateUserAsync(name);
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);
		var id = await client.LoginAsync(name);

		var (_, listed) = await fixture.RunAsync($"users {id} sessions list --state online --types {(int)SessionType.Api}");
		var sessionId = listed.Split('\n').Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]).First(word => Guid.TryParse(word, out _));
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
	public async Task Sessions_DeleteAll_ClosesEverySessionOfTheAccount()
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
	public async Task Delete_RemovesTheAccountForGood()
	{
		var user = await NewAsync("del");

		var (code, _) = await fixture.RunAsync($"users {user.Id} delete");
		var (shown, text) = await fixture.RunAsync($"users {user.Id}");

		Assert.Equal(0, code);
		Assert.Equal(1, shown);
		Assert.Contains("notFound", text);
	}

	[Fact]
	public async Task Avatar_OfAnUnknownAccount_IsNotFound()
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

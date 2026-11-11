using System.Text;
using Basil.Application.Shared;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Capabilities;
using Basil.Application.Users.Objects;
using Basil.Domain.Cryptography;
using Basil.Domain.Shared;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Capabilities;

/// <summary>A scope made from a reference refuses an unprivileged caller the same way whether or not the account exists.</summary>
public sealed class ScopedUserReferenceTests
{
	private const int MissingId = 9_999_999;

	private static readonly Dictionary<string, (UserPrivileges Needed, Func<ScopedUser, Task<UserOutcome>> Act)>
		Views = new()
		{
			["Rename"] = (UserPrivileges.Owner, scope => scope.Profile.RenameAsync("Renamed")),
			["SetCountry"] = (UserPrivileges.Owner, scope => scope.Profile.SetCountryAsync(Country.Vn)),
			["SetAvatar"] = (UserPrivileges.Owner, scope => scope.Profile.SetAvatarAsync(new MemoryStream())),
			["RemoveAvatar"] = (UserPrivileges.Owner, scope => scope.Profile.RemoveAvatarAsync()),
			["ChangePassword"] = (UserPrivileges.Owner,
				scope => scope.Credentials.ChangePasswordAsync(new Md5(Encoding.UTF8.GetBytes("new")), null)),
			["SetPrivileges"] = (UserPrivileges.Owner, scope => scope.Privileges.SetAsync(UserPrivilegesExtensions.Default)),
			["Delete"] = (UserPrivileges.Owner, scope => scope.DeleteAsync()),
			["Impose"] = (UserPrivileges.Moderator,
				scope => scope.Silence.ImposeAsync(DateTimeOffset.UnixEpoch.AddYears(100))),
			["Lift"] = (UserPrivileges.Moderator, scope => scope.Silence.LiftAsync()),
			["ListSessions"] = (UserPrivileges.Moderator,
				async scope => (await scope.Sessions.ListAsync(new PageRequest(0, 10))).Result),
			["GetSession"] = (UserPrivileges.Moderator, async scope => (await scope.Sessions.GetAsync(1)).Result),
			["ListConnections"] = (UserPrivileges.Moderator,
				async scope => (await scope.Sessions.ListConnectionsAsync(1, new PageRequest(0, 10))).Result),
			["CloseSession"] = (UserPrivileges.Owner, scope => scope.Sessions.CloseAsync(1)),
			["CloseSessions"] = (UserPrivileges.Owner, scope => scope.Sessions.CloseAllAsync())
		};

	public static TheoryData<string> ViewNames => [.. Views.Keys];

	[Theory]
	[MemberData(nameof(ViewNames))]
	public async Task ViewNoPrivilege_AlwaysDenied(string view)
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var existing = await app.CreateUserAsync("Existing", "password");
		var (_, act) = Views[view];

		var ofExisting = await act(app.Users.ScopeTo(actor, UserReference.ById(existing.Id)));
		var ofMissing = await act(app.Users.ScopeTo(actor, UserReference.ById(MissingId)));

		Assert.False(ofMissing == UserOutcome.Ok);
		Assert.Equal(UserOutcome.Denied, ofMissing);
		Assert.Equal(ofExisting, ofMissing);
	}

	[Theory]
	[MemberData(nameof(ViewNames))]
	public async Task ViewMissing_NotFound(string view)
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Staff", "password", UserPrivilegesExtensions.All));

		var result = await Views[view].Act(app.Users.ScopeTo(actor, UserReference.ById(MissingId)));

		Assert.Equal(UserOutcome.NotFound, result);
		Assert.NotEqual(UserOutcome.Denied, result);
	}

	[Fact]
	public async Task MissingRelationship_NotFound()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Friendly", "password"));
		var scope = app.Users.ScopeTo(actor, UserReference.ById(MissingId));

		Assert.Equal(UserOutcome.NotFound, (await scope.Relationships.AddFriendAsync()));
		Assert.Equal(UserOutcome.NotFound, (await scope.Relationships.BlockAsync()));
	}

	[Fact]
	public async Task IrcOpenOther_NotSelf()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Irc", "password"));
		var existing = await app.CreateUserAsync("IrcOther", "password");

		var ofExisting = await app.Users.ScopeTo(actor, UserReference.ById(existing.Id)).Sessions.OpenIrcAsync("x");
		var ofMissing = await app.Users.ScopeTo(actor, UserReference.ById(MissingId)).Sessions.OpenIrcAsync("x");

		Assert.Equal(UserOutcome.NotSelf, ofMissing.Result);
		Assert.Equal(ofExisting.Result, ofMissing.Result);
	}

	[Fact]
	public async Task NameLookup_IgnoresCaseUnderscores()
	{
		using var app = new TestApplication();
		var account = await app.CreateUserAsync("Some Name", "password");

		Assert.True(UserReference.ByName("some_name").Names(account));
		Assert.False(UserReference.ById(account.Id + 1).Names(account));
	}
}
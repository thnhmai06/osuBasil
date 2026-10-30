using Basil.Domain.Users;

namespace Basil.Host.Rest.Tests.Multiplayer;

/// <summary>Management of general channels.</summary>
[Collection("REST host")]
public sealed class ChannelTests(RestHostFixture fixture)
{
	private static readonly UserPermissions Manager = UserPermissionsGroups.Default | UserPermissions.ChatManageChannels;

	[Fact]
	public async Task List_WithoutAToken_Answers401()
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (status, _) = await client.CallAsync(HttpMethod.Get, "/v1/channels");

		Assert.Equal(401, status);
	}

	[Fact]
	public async Task Create_ByAManager_Answers201_AndByAPlainUser_Answers403WithTheMissingMask()
	{
		var (manager, _) = await fixture.SignInAsync("ChanManager", Manager);
		var (plain, _) = await fixture.SignInAsync("ChanPlain");
		using (manager)
		using (plain)
		{
			var (created, _) = await manager.CallAsync(HttpMethod.Post, "/v1/channels", new { name = "#rest1", topic = "hi" });
			var (denied, envelope) = await plain.CallAsync(HttpMethod.Post, "/v1/channels", new { name = "#rest2" });

			Assert.Equal(201, created);
			Assert.Equal(403, denied);
			Assert.Equal((ulong)UserPermissions.ChatManageChannels, (ulong)envelope!["errors"]!["missing"]!);
		}
	}

	[Fact]
	public async Task Create_WithTheNameOfAnOpenChannel_Answers409()
	{
		var (manager, _) = await fixture.SignInAsync("ChanDup", Manager);
		using (manager)
		{
			await manager.CallAsync(HttpMethod.Post, "/v1/channels", new { name = "#restdup" });

			var (status, _) = await manager.CallAsync(HttpMethod.Post, "/v1/channels", new { name = "#restdup" });

			Assert.Equal(409, status);
		}
	}

	[Fact]
	public async Task Create_WithAnInvalidName_Answers422()
	{
		var (manager, _) = await fixture.SignInAsync("ChanBad", Manager);
		using (manager)
		{
			var (status, _) = await manager.CallAsync(HttpMethod.Post, "/v1/channels", new { name = "no hash" });

			Assert.Equal(422, status);
		}
	}

	[Fact]
	public async Task Patch_ChangesTheTopic_AndAHiddenChannelIsListedOnlyForManagers()
	{
		var (manager, _) = await fixture.SignInAsync("ChanPatch", Manager);
		var (plain, _) = await fixture.SignInAsync("ChanViewer");
		using (manager)
		using (plain)
		{
			await manager.CallAsync(HttpMethod.Post, "/v1/channels", new { name = "#resthid", visible = false });

			var (patched, envelope) = await manager.CallAsync(HttpMethod.Patch, "/v1/channels/%23resthid", new { topic = "new" });
			var (_, forManager) = await manager.CallAsync(HttpMethod.Get, "/v1/channels");
			var (_, forPlain) = await plain.CallAsync(HttpMethod.Get, "/v1/channels");

			Assert.Equal(200, patched);
			Assert.Equal("new", (string)envelope!["data"]!["topic"]!);
			Assert.Contains(forManager!["data"]!.AsArray(), c => (string)c!["name"]! == "#resthid");
			Assert.DoesNotContain(forPlain!["data"]!.AsArray(), c => (string)c!["name"]! == "#resthid");
		}
	}

	[Fact]
	public async Task Delete_RemovesTheChannel_AndAnUnknownChannelAnswers404()
	{
		var (manager, _) = await fixture.SignInAsync("ChanDel", Manager);
		using (manager)
		{
			await manager.CallAsync(HttpMethod.Post, "/v1/channels", new { name = "#restdel" });

			var (deleted, _) = await manager.CallAsync(HttpMethod.Delete, "/v1/channels/%23restdel");
			var (again, _) = await manager.CallAsync(HttpMethod.Delete, "/v1/channels/%23restdel");

			Assert.Equal(200, deleted);
			Assert.Equal(404, again);
		}
	}

	[Fact]
	public async Task Patch_WithoutTheManagePermission_AnswersTheSameWhetherOrNotTheChannelExists()
	{
		var (manager, _) = await fixture.SignInAsync("ChanPatchDenied", Manager);
		var (plain, _) = await fixture.SignInAsync("ChanPatchPlain");
		using (manager)
		using (plain)
		{
			await manager.CallAsync(HttpMethod.Post, "/v1/channels", new { name = "#restpatchhid", visible = false });

			var (exists, hidden) = await plain.CallAsync(HttpMethod.Patch, "/v1/channels/%23restpatchhid", new { topic = "x" });
			var (missing, ghost) = await plain.CallAsync(HttpMethod.Patch, "/v1/channels/%23restpatchghost", new { topic = "x" });

			Assert.Equal(403, exists);
			Assert.Equal(exists, missing);
			Assert.Equal((ulong)UserPermissions.ChatManageChannels, (ulong)hidden!["errors"]!["missing"]!);
			Assert.Equal((ulong)UserPermissions.ChatManageChannels, (ulong)ghost!["errors"]!["missing"]!);
		}
	}

	[Fact]
	public async Task Delete_WithoutTheManagePermission_AnswersTheSameWhetherOrNotTheChannelExists()
	{
		var (manager, _) = await fixture.SignInAsync("ChanDelDenied", Manager);
		var (plain, _) = await fixture.SignInAsync("ChanDelPlain");
		using (manager)
		using (plain)
		{
			await manager.CallAsync(HttpMethod.Post, "/v1/channels", new { name = "#restdeletehid", visible = false });

			var (exists, hidden) = await plain.CallAsync(HttpMethod.Delete, "/v1/channels/%23restdeletehid");
			var (missing, ghost) = await plain.CallAsync(HttpMethod.Delete, "/v1/channels/%23restdeleteghost");

			Assert.Equal(403, exists);
			Assert.Equal(exists, missing);
			Assert.Equal((ulong)UserPermissions.ChatManageChannels, (ulong)hidden!["errors"]!["missing"]!);
			Assert.Equal((ulong)UserPermissions.ChatManageChannels, (ulong)ghost!["errors"]!["missing"]!);
		}
	}
}

using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Common.Common;
using Basil.Application.Content.Persistence;
using Basil.Domain.Beatmaps;
using Basil.Host.Cli.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Cli.Tests.Commands;

/// <summary>The beatmap, score, settings, content, announcement and diagnostics commands.</summary>
[Collection("CLI host")]
public sealed class ContentCommandTests(CliFixture fixture)
{
	private T Get<T>() where T : notnull
	{
		return fixture.Host.Services.GetRequiredService<T>();
	}

	private static string SamplePath(string name)
	{
		return Path.Combine(AppContext.BaseDirectory, "Assets", "Sets", name);
	}

	[Fact]
	public async Task Beatmapsets_Create_ImportsAnOsz_ThenShowListAndBeatmapsPrintIt()
	{
		var (code, text) = await fixture.RunAsync($"beatmapsets create --file {SamplePath("hopes-and-dreams-catch.osz")}");
		var set = (await Get<IBeatmapsetRepository>().ListAsync(new BeatmapQuery(), new PageRequest(0, 100)))
			.Items.First(item => item.Metadata.Title.Contains("Hopes", StringComparison.OrdinalIgnoreCase));
		var maps = await Get<IBeatmapRepository>().ListAsync(set);

		var (shown, shownText) = await fixture.RunAsync($"beatmapsets {set.Id}");
		var (listed, listedText) = await fixture.RunAsync("beatmapsets list --query hopes");
		var (map, mapText) = await fixture.RunAsync($"beatmaps {maps[0].Id}");
		var (again, againText) = await fixture.RunAsync($"beatmapsets create --file {SamplePath("hopes-and-dreams-catch.osz")}");

		Assert.True(code == 0 || code == 1, text);
		Assert.Equal(0, shown);
		Assert.Contains("beatmaps:", shownText);
		Assert.Equal(0, listed);
		Assert.Contains(set.Metadata.Title, listedText);
		Assert.Equal(0, map);
		Assert.Contains("stars:", mapText);
		Assert.Equal(1, again);
		Assert.Contains("alreadyExists", againText);
	}

	[Fact]
	public async Task Beatmapsets_Set_ChangesStatusVisibilityAndLock_AndABadStatusIsInvalid()
	{
		var maps = await fixture.Host.ImportSampleAsync("Sets/selene-ui-mania.osz");
		var set = maps[0].Beatmapset;

		var (code, _) = await fixture.RunAsync($"beatmapsets {set.Id} set --status loved --visible false --locked true");
		var stored = (await Get<IBeatmapsetRepository>().GetAsync(set.Id))!;
		var (status, visible, locked) = (stored.Status, stored.Visible, stored.Locked);
		var (bad, _) = await fixture.RunAsync($"beatmapsets {set.Id} set --status bogus");
		await fixture.RunAsync($"beatmapsets {set.Id} set --visible true --locked false");

		Assert.Equal(0, code);
		Assert.Equal(BeatmapsetStatus.Loved, status);
		Assert.False(visible);
		Assert.True(locked);
		Assert.Equal(2, bad);
	}

	[Fact]
	public async Task Beatmapsets_Delete_RemovesTheSet()
	{
		var maps = await fixture.Host.ImportSampleAsync("Sets/mosi-mosi-taiko.osz");
		var set = maps[0].Beatmapset;

		var (code, _) = await fixture.RunAsync($"beatmapsets {set.Id} delete");
		var (again, _) = await fixture.RunAsync($"beatmapsets {set.Id}");

		Assert.Equal(0, code);
		Assert.Equal(1, again);
		await fixture.Host.ImportSampleAsync("Sets/mosi-mosi-taiko.osz");
	}

	[Fact]
	public async Task Beatmapsets_Refresh_ScansTheStorageAndSaysWhatChanged()
	{
		var (code, text) = await fixture.RunAsync("beatmapsets refresh");

		Assert.Equal(0, code);
		Assert.Matches(@"Scanned: \d+ taken in, \d+ updated, \d+ forgotten\.", text);
	}

	[Fact]
	public async Task Beatmaps_AndScores_UnknownIds_AreNotFound_AndAnEmptyScoreListPrints()
	{
		Assert.Equal(1, (await fixture.RunAsync("beatmaps 999999")).Code);
		Assert.Equal(1, (await fixture.RunAsync("scores 999999")).Code);
		Assert.Equal(1, (await fixture.RunAsync("scores list --beatmap 999999")).Code);
		Assert.Equal(1, (await fixture.RunAsync("scores list --user 999999")).Code);

		var (code, text) = await fixture.RunAsync("scores list");

		Assert.Equal(0, code);
		Assert.Contains("0 of 0", text);
	}

	[Fact]
	public async Task Settings_ShowPrintsEveryCategoryAsDottedFields_AndACategoryAlone()
	{
		var (code, text) = await fixture.RunAsync("settings");
		var (_, category) = await fixture.RunAsync("settings rateLimits");

		Assert.Equal(0, code);
		Assert.Contains("locks.accounts:", text);
		Assert.Contains("rateLimits.chat.count:", text);
		Assert.Contains("chat.count:", category);
		Assert.Equal(1, (await fixture.RunAsync("settings nothing")).Code);
	}

	[Fact]
	public async Task Settings_Set_ChangesANestedFieldAndKeepsTheRest()
	{
		var before = await Get<ISettingsRepository>().GetAsync();

		var (code, _) = await fixture.RunAsync("settings rateLimits set --chat.count 7");
		var after = await Get<ISettingsRepository>().GetAsync();
		await fixture.RunAsync($"settings rateLimits set --chat.count {before.RateLimits.Chat.Count}");

		Assert.Equal(0, code);
		Assert.Equal(7, after.RateLimits.Chat.Count);
		Assert.Equal(before.RateLimits.Chat.Window, after.RateLimits.Chat.Window);
		Assert.Equal(before.RateLimits.Login, after.RateLimits.Login);
	}

	[Fact]
	public async Task Settings_Set_Locks_TakesBooleans_AndAnInvalidValueIsInvalid()
	{
		var before = (await Get<ISettingsRepository>().GetAsync()).Locks;

		var (code, _) = await fixture.RunAsync("settings locks set --accounts true --rooms false");
		var locks = (await Get<ISettingsRepository>().GetAsync()).Locks;
		var (bad, _) = await fixture.RunAsync("settings rateLimits set --chat.count 0");
		await fixture.RunAsync($"settings locks set --accounts {before.HasFlag(Locks.Accounts)} --rooms {before.HasFlag(Locks.Rooms)}");

		Assert.Equal(0, code);
		Assert.True(locks.HasFlag(Locks.Accounts));
		Assert.False(locks.HasFlag(Locks.Rooms));
		Assert.Equal(2, bad);
	}

	[Fact]
	public async Task Settings_Set_WithoutAField_IsInvalid()
	{
		Assert.Equal(2, (await fixture.RunAsync("settings general set")).Code);
	}

	[Fact]
	public async Task The_console_passes_a_lock_as_a_manager_does()
	{
		await fixture.RunAsync("settings locks set --accounts true");
		var name = CliFixture.Name("locked");

		var (code, _) = await fixture.RunAsync($"users create --username {name} --password pw");
		await fixture.RunAsync("settings locks set --accounts false");

		Assert.Equal(0, code);
	}

	[Fact]
	public async Task Faqs_SetShowListDelete_RoundTripATextFile()
	{
		var entry = CliFixture.Name("faq");
		var file = Path.GetTempFileName();
		await File.WriteAllTextAsync(file, "How to play?\nJust do.");
		try
		{
			var (set, _) = await fixture.RunAsync($"faqs {entry} set --file {file}");
			var (shown, shownText) = await fixture.RunAsync($"faqs {entry}");
			var (_, listed) = await fixture.RunAsync("faqs list");
			var (deleted, _) = await fixture.RunAsync($"faqs {entry} delete");
			var (after, _) = await fixture.RunAsync($"faqs {entry}");

			Assert.Equal(0, set);
			Assert.Equal(0, shown);
			Assert.Contains("How to play?", shownText);
			Assert.Contains(entry, listed);
			Assert.Equal(0, deleted);
			Assert.Equal(1, after);
		}
		finally
		{
			File.Delete(file);
		}
	}

	[Fact]
	public async Task Menu_IconAndSeasonalsAndBanners_ListWhenEmpty_AndRefuseUnknowns()
	{
		Assert.Equal(0, (await fixture.RunAsync("menu icon")).Code);
		Assert.Equal(0, (await fixture.RunAsync("menu banners list")).Code);
		Assert.Equal(0, (await fixture.RunAsync("menu seasonals list")).Code);
		Assert.Equal(1, (await fixture.RunAsync("menu banners nothing.png delete")).Code);
		Assert.Equal(1, (await fixture.RunAsync("menu seasonals nothing.png delete")).Code);
	}

	[Fact]
	public async Task Announce_ToNobodyOnline_SendsZero_AndAnUnknownRecipientIsNotFound()
	{
		var (code, text) = await fixture.RunAsync("announce create --text \"hello all\"");
		var (bad, badText) = await fixture.RunAsync("announce create --text hi --to 999999");

		Assert.Equal(0, code);
		Assert.Equal("Sent to 0 clients.", text);
		Assert.Equal(1, bad);
		Assert.Contains("No such user: 999999", badText);
	}

	[Fact]
	public async Task Screenshots_ListIsEmpty_AndAnUnknownScreenshotIsNotFound()
	{
		var (code, text) = await fixture.RunAsync("screenshots list");

		Assert.Equal(0, code);
		Assert.Contains("0 of 0", text);
		Assert.Equal(1, (await fixture.RunAsync("screenshots nothing.jpg delete")).Code);
	}

	[Fact]
	public async Task Diagnostics_Actions_Run()
	{
		Assert.Equal(0, (await fixture.RunAsync("diagnostics gc collect")).Code);
		Assert.Equal(0, (await fixture.RunAsync("diagnostics caches clear")).Code);
		Assert.Equal(0, (await fixture.RunAsync("diagnostics exceptions reset")).Code);
	}
}

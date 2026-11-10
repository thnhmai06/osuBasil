using System.Text;
using Basil.Domain.Beatmaps;
using Basil.Domain.Client;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Infrastructure.Scores.Storage;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Scores.Tests.Storage;

/// <summary>Contract tests of FileReplayStorage's replay saves.</summary>
public sealed class FileReplayStorageTests : IDisposable
{
	private const int ScoreId = 17;
	private static readonly OsuVersion Version = new(new DateOnly(2026, 7, 11), null, OsuVersionStream.Stable);
	private readonly FakeBeatmapRepository _beatmaps;

	private readonly TempData _data = new();
	private readonly FileReplayStorage _replays;
	private readonly FakeUserRepository _users;

	public FileReplayStorageTests()
	{
		_users = new FakeUserRepository(StoredUsers.Regular);
		_beatmaps = new FakeBeatmapRepository(StoredBeatmaps.Beatmap());
		_replays = new FileReplayStorage(_data.Paths, _users, _beatmaps);
	}

	private string ReplayDirectory => _data.Paths.Replays.Path;

	public void Dispose()
	{
		_data.Dispose();
	}

	[Fact]
	public async Task ReplacedReplay_keeps_only_the_new_name()
	{
		await _replays.SaveAsync(Score(), Version, Bytes("first"));

		_users.User = StoredUsers.Renamed;
		await _replays.SaveAsync(Score(), Version, Bytes("second"));

		Assert.Equal("second", await TextAsync());
		Assert.Single(Directory.GetFiles(ReplayDirectory));
	}

	[Fact]
	public async Task FailedSaveAfterRename_keeps_the_previous_replay()
	{
		await _replays.SaveAsync(Score(), Version, Bytes("first"));

		_users.User = StoredUsers.Renamed;
		await Assert.ThrowsAsync<IOException>(() =>
			_replays.SaveAsync(Score(), Version, new TestStreams.Failing()));

		Assert.Equal("first", await TextAsync());
		Assert.Single(Directory.GetFiles(ReplayDirectory));
	}

	[Fact]
	public async Task OpenAsync_returns_null_for_a_score_without_replay()
	{
		Assert.Null(await _replays.OpenAsync(Score(), TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_saved_replay_opens_with_the_bytes_it_was_saved_with()
	{
		await _replays.SaveAsync(Score(), Version, Bytes("first"));

		Assert.Equal("first", await TextAsync());
	}

	[Fact]
	public async Task A_replay_is_named_like_an_osu_replay_export()
	{
		await _replays.SaveAsync(Score(), Version, Bytes("replay"));

		Assert.Equal(
			["17 Regular - Camellia - Exit This Earth's Atomosphere [Primordial Nucleosynthesis] (2026-03-05) Osu.osr"],
			Directory.GetFiles(ReplayDirectory).Select(file => Path.GetFileName(file)));
	}

	[Theory]
	[InlineData(GameMode.Standard, "Osu")]
	[InlineData(GameMode.Taiko, "Taiko")]
	[InlineData(GameMode.Catch, "CatchTheBeat")]
	[InlineData(GameMode.Mania, "OsuMania")]
	public async Task A_replay_name_ends_with_the_name_osu_gives_the_game_mode(GameMode mode, string modeName)
	{
		await _replays.SaveAsync(Score(mode: mode), Version, Bytes("replay"));

		var name = Path.GetFileName(Assert.Single(Directory.GetFiles(ReplayDirectory)));
		Assert.EndsWith($" {modeName}.osr", name);
	}

	[Fact]
	public async Task A_replay_is_found_by_its_score_id_alone_and_not_by_a_longer_id_that_starts_with_it()
	{
		await _replays.SaveAsync(Score(170), Version, Bytes("longer id"));

		Assert.Null(await _replays.OpenAsync(Score(), TestContext.Current.CancellationToken));

		await _replays.SaveAsync(Score(), Version, Bytes("shorter id"));

		Assert.Equal("shorter id", await TextAsync());
		Assert.Equal("longer id", await TextAsync(Score(170)));
	}

	[Fact]
	public async Task A_replay_of_a_score_whose_player_is_unknown_is_still_stored_and_found()
	{
		_users.User = null;

		await _replays.SaveAsync(Score(), Version, Bytes("replay"));

		Assert.Equal("replay", await TextAsync());
	}

	[Fact]
	public async Task A_replay_of_a_score_whose_beatmap_is_unknown_is_still_stored_and_found()
	{
		_beatmaps.Beatmap = null;

		await _replays.SaveAsync(Score(), Version, Bytes("replay"));

		Assert.Equal("replay", await TextAsync());
	}

	[Fact]
	public async Task A_replay_name_has_no_character_a_file_name_may_not_have()
	{
		SetBeatmapNames("AC/DC", "What? \"Yes\" <no> | *maybe* : C:\\x");

		await _replays.SaveAsync(Score(), Version, Bytes("replay"));

		var name = Path.GetFileName(Assert.Single(Directory.GetFiles(ReplayDirectory)));
		Assert.DoesNotContain(name, character => "<>:\"/\\|?*".Contains(character));
		Assert.StartsWith("17 Regular - ", name);
		Assert.EndsWith("Osu.osr", name);
		Assert.Equal("replay", await TextAsync());
	}

	[Fact]
	public async Task A_replay_name_too_long_for_the_file_system_is_shortened_by_bytes_without_splitting_a_character()
	{
		// 100 emoji are 400 UTF-8 bytes in surrogate pairs, so the cut falls inside a character unless it is placed.
		SetBeatmapNames("Camellia", string.Concat(Enumerable.Repeat("\U0001F600", 100)));

		await _replays.SaveAsync(Score(), Version, Bytes("replay"));

		var name = Path.GetFileName(Assert.Single(Directory.GetFiles(ReplayDirectory)));
		Assert.True(Encoding.UTF8.GetByteCount(name) <= 255,
			$"The name takes {Encoding.UTF8.GetByteCount(name)} bytes.");
		Assert.StartsWith("17 Regular - Camellia - ", name);
		Assert.EndsWith(".osr", name);
		Assert.Equal(name, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(name)));
		Assert.Equal("replay", await TextAsync());
	}

	[Fact]
	public async Task A_replay_stored_under_a_shortened_name_is_replaced_by_the_next_save()
	{
		SetBeatmapNames("Camellia", new string('x', 400));
		await _replays.SaveAsync(Score(), Version, Bytes("first"));

		await _replays.SaveAsync(Score(), Version, Bytes("second"));

		Assert.Equal("second", await TextAsync());
		Assert.Single(Directory.GetFiles(ReplayDirectory));
	}

	[Fact]
	public async Task
		A_saved_replay_opens_as_a_complete_file_that_records_the_client_version_and_ends_with_the_score_id()
	{
		await _replays.SaveAsync(Score(), Version, Bytes("frames"));

		var stream = await _replays.OpenAsync(Score(), TestContext.Current.CancellationToken);
		Assert.NotNull(stream);
		using var file = new MemoryStream();
		await using (stream)
		{
			await stream.CopyToAsync(file, TestContext.Current.CancellationToken);
		}

		var bytes = file.ToArray();
		Assert.Equal((byte)GameMode.Standard, bytes[0]);
		Assert.Equal(20260711, BitConverter.ToInt32(bytes, 1));
		Assert.Equal(ScoreId, BitConverter.ToInt64(bytes, bytes.Length - sizeof(long)));
	}

	[Fact]
	public async Task OpenFramesAsync_gives_a_seekable_stream_as_long_as_the_frames()
	{
		await _replays.SaveAsync(Score(), Version, Bytes("frames"));

		var stream = await _replays.OpenFramesAsync(Score(), TestContext.Current.CancellationToken);

		Assert.NotNull(stream);
		await using (stream)
		{
			Assert.True(stream.CanSeek);
			Assert.Equal("frames".Length, stream.Length);
		}
	}

	[Fact]
	public async Task OpenFramesAsync_returns_null_for_a_score_without_replay()
	{
		Assert.Null(await _replays.OpenFramesAsync(Score(), TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task OpenFramesAsync_returns_null_for_a_stored_file_that_is_not_a_replay()
	{
		Directory.CreateDirectory(ReplayDirectory);
		await File.WriteAllBytesAsync(Path.Combine(ReplayDirectory, $"{ScoreId}.osr"), Bytes30(),
			TestContext.Current.CancellationToken);

		Assert.Null(await _replays.OpenFramesAsync(Score(), TestContext.Current.CancellationToken));
	}

	private static byte[] Bytes30()
	{
		return [.. Enumerable.Range(0, 30).Select(i => (byte)i)];
	}

	private void SetBeatmapNames(string artist, string title)
	{
		var beatmap = StoredBeatmaps.Beatmap();
		beatmap.Beatmapset.Metadata = new BeatmapsetMetadata
			{ Artist = artist, Title = title, Creator = "ProfessionalBox" };
		_beatmaps.Beatmap = beatmap;
	}

	/// <summary>The frames as text, as the storage's frames stream delivers them; empty when no replay is stored.</summary>
	private async ValueTask<string> TextAsync(ScoreRecord? score = null)
	{
		var stream = await _replays.OpenFramesAsync(score ?? Score(), TestContext.Current.CancellationToken);
		if (stream is null) return string.Empty;

		await using (stream)
		{
			return await new StreamReader(stream).ReadToEndAsync(TestContext.Current.CancellationToken);
		}
	}

	private static ScoreRecord Score(int id = ScoreId, GameMode mode = GameMode.Standard)
	{
		return new ScoreRecord
		{
			Id = id,
			Value = new ScoreData(8, new Md5("d41d8cd98f00b204e9800998ecf8427e"), mode, GameMods.NoMod,
				new ScoreState(new HitCounts(100, 10, 2, 1, 3, 0), 51_000, 500, 312, false), Grade.A, true,
				new DateTimeOffset(2026, 3, 5, 12, 0, 0, TimeSpan.Zero))
		};
	}

	private static MemoryStream Bytes(string text)
	{
		return new MemoryStream(Encoding.UTF8.GetBytes(text));
	}
}
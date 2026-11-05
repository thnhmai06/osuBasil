using Basil.Domain.Client;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Infrastructure.Scores.Storage;

namespace Basil.Infrastructure.Scores.Tests.Storage;

/// <summary>
///     Contract tests of the <c>.osr</c> file: its bytes are pinned against an independent oracle (the layout and the
///     replay checksum of bancho.py's <c>build_full_replay</c>), not against what the code under test produces.
/// </summary>
public sealed class ReplayFileTests
{
	private const string Oracle =
		"00672735010B2063306161626263636464656566663030313132323333343435353636373738380B08636F6F6B69657A690B203034323533623031303562373966383737666633646464646666333932653335EA01050003006400020001004E61BC00F40100480000000080E9E85FEB1BD9081E000000000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D2B02000000000000";

	/// <summary>Where the six 16-bit hit counts start in <see cref="Oracle" />: after the three strings of its header.</summary>
	private const int HitCountsOffset = 83;

	private static readonly OsuVersion Version = new(new DateOnly(2026, 7, 11), null, OsuVersionStream.Stable);
	private static readonly byte[] Frames = [.. Enumerable.Range(0, 30).Select(i => (byte)i)];

	private static byte[] Build(ScoreRecord score, string playerName, OsuVersion version, byte[] frames)
	{
		using var output = new MemoryStream();
		ReplayFile.WriteAsync(output, score, playerName, version, new MemoryStream(frames)).GetAwaiter().GetResult();
		return output.ToArray();
	}

	[Fact]
	public void Build_writes_the_header_the_frames_and_the_score_id_in_the_order_of_the_format()
	{
		var file = Build(Score(), "cookiezi", Version, Frames);

		Assert.Equal(Convert.FromHexString(Oracle), file);
	}

	[Fact]
	public void Build_adds_the_extra_value_of_target_practice_after_the_score_id()
	{
		var plain = Build(Score(), "cookiezi", Version, Frames);

		var target = Build(Score(GameMods.Target), "cookiezi", Version, Frames);

		Assert.Equal(plain.Length + sizeof(double), target.Length);
		Assert.Equal(new byte[sizeof(double)], target[^sizeof(double)..]);
	}

	[Fact]
	public void Build_keeps_a_hit_count_beyond_sixteen_bits_at_the_largest_value()
	{
		var counts = new HitCounts(70_000, 5, 3, 100, 2, 1);

		var file = Build(Score(counts: counts), "cookiezi", Version, Frames);

		Assert.Equal(ushort.MaxValue, BitConverter.ToUInt16(file, HitCountsOffset));
	}

	[Fact]
	public void TryFindFrames_finds_the_frames_a_built_file_carries()
	{
		var file = Build(Score(), "cookiezi", Version, Frames);

		Assert.True(ReplayFile.TryFindFrames(new MemoryStream(file), out var offset, out var length));
		Assert.Equal(Frames, file[(int)offset..(int)(offset + length)]);
	}

	[Fact]
	public void TryFindFrames_finds_the_frames_of_a_replay_with_no_player_and_no_beatmap()
	{
		var score = new ScoreRecord
		{
			Id = 1,
			Value = Score().Value with { BeatmapHash = null }
		};
		var file = Build(score, string.Empty, Version, Frames);

		Assert.True(ReplayFile.TryFindFrames(new MemoryStream(file), out var offset, out var length));
		Assert.Equal(Frames, file[(int)offset..(int)(offset + length)]);
	}

	[Fact]
	public void TryFindFrames_refuses_bytes_that_are_only_frames()
	{
		Assert.False(ReplayFile.TryFindFrames(new MemoryStream(Frames), out _, out _));
	}

	[Fact]
	public void TryFindFrames_refuses_a_file_cut_inside_its_header_or_its_frames()
	{
		var file = Build(Score(), "cookiezi", Version, Frames);

		Assert.False(ReplayFile.TryFindFrames(new MemoryStream(file[..100]), out _, out _));
		Assert.False(ReplayFile.TryFindFrames(new MemoryStream(file[..130]), out _, out _));
	}

	private static ScoreRecord Score(GameMods mods = (GameMods)72, HitCounts? counts = null)
	{
		var state = new ScoreState(counts ?? new HitCounts(490, 5, 3, 100, 2, 1), 12_345_678, 500, 500, false);
		return new ScoreRecord
		{
			Id = 555,
			Value = new ScoreData(1, new Md5("c0aabbccddeeff001122334455667788"), GameMode.Standard, mods, state,
				Grade.S, true, new DateTimeOffset(2021, 5, 20, 23, 59, 59, TimeSpan.Zero))
		};
	}
}

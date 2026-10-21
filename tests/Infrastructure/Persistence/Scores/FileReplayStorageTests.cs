using System.Text;
using Basil.Application.Persistence.Scores;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Infrastructure.Persistence.Common;
using Basil.Infrastructure.Persistence.Files;
using Basil.Infrastructure.Persistence.Scores;
using Basil.Infrastructure.Persistence.Tests.Support;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Persistence.Tests.Scores;

/// <summary>Contract tests of FileReplayStorage's replay saves.</summary>
public sealed class FileReplayStorageTests : IDisposable
{
	private const int ScoreId = 17;

	private readonly string _root = Path.Combine(Path.GetTempPath(), "basil-test-replays-" + Guid.NewGuid().ToString("N"));
	private readonly FakeUserRepository _users;
	private readonly FileReplayStorage _replays;

	public FileReplayStorageTests()
	{
		var paths = new DataPaths(Options.Create(new StorageOptions { DataDirectory = _root }));
		paths.CreateDirectories();
		_users = new FakeUserRepository(StoredUsers.Regular);
		_replays = new FileReplayStorage(paths, _users, new FakeBeatmapRepository(StoredBeatmaps.Beatmap()));
	}

	[Fact]
	public async Task ReplacedReplay_keeps_only_the_new_name()
	{
		await _replays.SaveAsync(Score(), Bytes("first"));

		_users.User = StoredUsers.Renamed;
		await _replays.SaveAsync(Score(), Bytes("second"));

		Assert.Equal("second", await TextAsync());
	}

	[Fact]
	public async Task FailedSaveAfterRename_keeps_the_previous_replay()
	{
		await _replays.SaveAsync(Score(), Bytes("first"));

		_users.User = StoredUsers.Renamed;
		await Assert.ThrowsAsync<IOException>(() => _replays.SaveAsync(Score(), Failing()));

		Assert.Equal("first", await TextAsync());
	}

	[Fact]
	public async Task OpenAsync_returns_null_for_a_score_without_replay()
	{
		Assert.Null(await _replays.OpenAsync(Score()));
		await _replays.SaveAsync(Score(), Bytes("first"));
		Assert.Equal("first", await TextAsync());
	}

	/// <summary>The bytes as text, as the storage's open stream delivers them; empty when no replay is stored.</summary>
	private async ValueTask<string> TextAsync()
	{
		var stream = await _replays.OpenAsync(Score());
		if (stream is null) return string.Empty;

		using (stream)
		{
			return new StreamReader(stream).ReadToEnd();
		}
	}

	private static ScoreRecord Score() => new()
	{
		Id = ScoreId,
		Value = new ScoreData(
			8, new Md5("d41d8cd98f00b204e9800998ecf8427e"), GameMode.Standard, GameMods.NoMod,
			new HitCounts(100, 10, 2, 1, 3, 0), 51_000, 500, 312, Grade.A, true, false, DateTimeOffset.UnixEpoch)
	};

	private static Stream Bytes(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

	private static Stream Failing() => new FailingStream();

	public void Dispose() => Directory.Delete(_root, recursive: true);

	/// <summary>A stream whose reads fail, the way a replay download that dies mid-transfer fails.</summary>
	private sealed class FailingStream : Stream
	{
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => 0; set { } }

		public override void Flush() { }

		public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Read failed.");

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}

using Basil.Application.Users.Storage;
using Basil.Domain.Users;
using Basil.Infrastructure.Users.Storage;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Users.Tests.Storage;

/// <summary>Contract tests of FileUserAvatarStorage, read through IUserAvatarStorage.</summary>
public sealed class FileUserAvatarStorageTests : IDisposable
{
	private static readonly UserRecord Alice = User(8, "Alice");
	private static readonly UserRecord Bob = User(9, "Bob");
	private readonly IUserAvatarStorage _avatars;

	private readonly TempData _data = new();

	public FileUserAvatarStorageTests()
	{
		_avatars = new FileUserAvatarStorage(_data.Paths);
	}

	public void Dispose()
	{
		_data.Dispose();
	}

	[Fact]
	public async Task NoAvatar_OpensNull()
	{
		Assert.Null(await _avatars.OpenAsync(Alice, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task SavedAvatar_OpensWithBytes()
	{
		await _avatars.SaveAsync(Alice, TestStreams.Of("alice"));

		Assert.Equal("alice",
			await TestStreams.ReadAllAsync(await _avatars.OpenAsync(Alice, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task ExistingAvatar_ReplacedForUser()
	{
		await _avatars.SaveAsync(Alice, TestStreams.Of("old and longer"));

		await _avatars.SaveAsync(Alice, TestStreams.Of("new"));

		Assert.Equal("new",
			await TestStreams.ReadAllAsync(await _avatars.OpenAsync(Alice, TestContext.Current.CancellationToken)));
		Assert.Single(TempData.FilesUnder(_data.Paths.Avatars.Path));
	}

	[Fact]
	public async Task OtherUserChanges_OwnAvatarKept()
	{
		await _avatars.SaveAsync(Alice, TestStreams.Of("alice"));
		await _avatars.SaveAsync(Bob, TestStreams.Of("bob"));

		await _avatars.DeleteAsync(Bob);

		Assert.Equal("alice",
			await TestStreams.ReadAllAsync(await _avatars.OpenAsync(Alice, TestContext.Current.CancellationToken)));
		Assert.Null(await _avatars.OpenAsync(Bob, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task FailedSave_KeepsStoredAvatar()
	{
		await _avatars.SaveAsync(Alice, TestStreams.Of("previous"));

		await Assert.ThrowsAsync<IOException>(() =>
			_avatars.SaveAsync(Alice, new TestStreams.Failing()));

		Assert.Equal("previous",
			await TestStreams.ReadAllAsync(await _avatars.OpenAsync(Alice, TestContext.Current.CancellationToken)));
		Assert.Single(TempData.FilesUnder(_data.Paths.Avatars.Path));
	}

	[Fact]
	public async Task DeletedAvatar_NoLongerStored()
	{
		await _avatars.SaveAsync(Alice, TestStreams.Of("alice"));

		await _avatars.DeleteAsync(Alice);

		Assert.Null(await _avatars.OpenAsync(Alice, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task MissingAvatarDelete_DoesNothing()
	{
		await _avatars.DeleteAsync(Alice);

		Assert.Null(await _avatars.OpenAsync(Alice, TestContext.Current.CancellationToken));
	}

	private static UserRecord User(int id, string name)
	{
		return new UserRecord { Id = id, Value = new UserData { Name = name } };
	}
}
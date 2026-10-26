using Basil.Application.Persistence.Users;
using Basil.Domain.Users;
using Basil.Infrastructure.Persistence.Content;
using Basil.Infrastructure.Persistence.Tests.Support;

namespace Basil.Infrastructure.Persistence.Tests.Content;

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
	public async Task A_user_without_an_avatar_opens_as_null()
	{
		Assert.Null(await _avatars.OpenAsync(Alice, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_saved_avatar_opens_with_the_bytes_it_was_saved_with()
	{
		await _avatars.SaveAsync(Alice, TestStreams.Of("alice"), TestContext.Current.CancellationToken);

		Assert.Equal("alice",
			await TestStreams.ReadAllAsync(await _avatars.OpenAsync(Alice, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task Saving_an_avatar_replaces_the_one_stored_for_the_user()
	{
		await _avatars.SaveAsync(Alice, TestStreams.Of("old and longer"), TestContext.Current.CancellationToken);

		await _avatars.SaveAsync(Alice, TestStreams.Of("new"), TestContext.Current.CancellationToken);

		Assert.Equal("new",
			await TestStreams.ReadAllAsync(await _avatars.OpenAsync(Alice, TestContext.Current.CancellationToken)));
		Assert.Single(TempData.FilesUnder(_data.Paths.Avatars.Path));
	}

	[Fact]
	public async Task A_user_keeps_their_own_avatar_whatever_another_user_saves_or_deletes()
	{
		await _avatars.SaveAsync(Alice, TestStreams.Of("alice"), TestContext.Current.CancellationToken);
		await _avatars.SaveAsync(Bob, TestStreams.Of("bob"), TestContext.Current.CancellationToken);

		await _avatars.DeleteAsync(Bob, TestContext.Current.CancellationToken);

		Assert.Equal("alice",
			await TestStreams.ReadAllAsync(await _avatars.OpenAsync(Alice, TestContext.Current.CancellationToken)));
		Assert.Null(await _avatars.OpenAsync(Bob, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_save_that_fails_keeps_the_stored_avatar()
	{
		await _avatars.SaveAsync(Alice, TestStreams.Of("previous"), TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<IOException>(() =>
			_avatars.SaveAsync(Alice, new TestStreams.Failing(), TestContext.Current.CancellationToken));

		Assert.Equal("previous",
			await TestStreams.ReadAllAsync(await _avatars.OpenAsync(Alice, TestContext.Current.CancellationToken)));
		Assert.Single(TempData.FilesUnder(_data.Paths.Avatars.Path));
	}

	[Fact]
	public async Task A_deleted_avatar_is_no_longer_stored()
	{
		await _avatars.SaveAsync(Alice, TestStreams.Of("alice"), TestContext.Current.CancellationToken);

		await _avatars.DeleteAsync(Alice, TestContext.Current.CancellationToken);

		Assert.Null(await _avatars.OpenAsync(Alice, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Deleting_a_missing_avatar_does_nothing()
	{
		await _avatars.DeleteAsync(Alice, TestContext.Current.CancellationToken);

		Assert.Null(await _avatars.OpenAsync(Alice, TestContext.Current.CancellationToken));
	}

	private static UserRecord User(int id, string name)
	{
		return new UserRecord { Id = id, Value = new UserData { Name = name } };
	}
}
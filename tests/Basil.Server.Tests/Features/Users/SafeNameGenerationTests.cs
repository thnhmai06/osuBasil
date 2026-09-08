using Basil.Domain.Users;

namespace Basil.Server.Tests.Features.Users;

/// <summary>
///     SQLite generates SafeName now, so the database's expression and the in-memory rule used by
///     the session registries must agree for every username the server can actually store.
/// </summary>
/// <remarks>
///     They diverge only outside ASCII -- SQLite's lower() is ASCII-only while ToLowerInvariant is
///     not -- which is unreachable because ValidateUsername rejects non-ASCII names. That rejection
///     is the invariant holding these two implementations together, so it is tested alongside.
/// </remarks>
public class SafeNameGenerationTests
{
	[Theory]
	[InlineData("Peppy")]
	[InlineData("pe ppy")]
	[InlineData("PE_PPY")]
	[InlineData("AB-CD")]
	[InlineData("[Box] x")]
	[InlineData("Z9 _-[]")]
	[InlineData("abc")]
	[InlineData("A1_-[] b")]
	public async Task GeneratedSafeNameMatchesMakeSafeName(string name)
	{
		await using var db = await UsersFixture.CreateAsync();
		var id = await db.InsertUserAsync(name);

		var stored = await db.QuerySafeNameAsync(id);

		Assert.Equal(User.MakeSafeName(name), stored);
	}
}

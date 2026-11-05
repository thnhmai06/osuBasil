using Basil.Testing;

namespace Basil.Application.Users.Tests.Registry;

/// <summary>A login whose session cannot be stored fails to the caller and leaves nobody online.</summary>
public sealed class LoginRecordingFailureTests
{
	[Fact]
	public async Task A_login_whose_session_cannot_be_recorded_fails_and_leaves_the_user_offline()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Unrecorded", "password");
		app.SessionRecordFailure = new InvalidOperationException("the database refused the session");

		await Assert.ThrowsAsync<InvalidOperationException>(() => app.AttemptLoginAsync("Unrecorded", "password"));

		Assert.DoesNotContain(user, app.Users.Online.Keys);
	}
}
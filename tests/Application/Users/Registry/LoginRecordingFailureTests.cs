using Basil.Testing;

namespace Basil.Application.Users.Tests.Registry;

public sealed class LoginRecordingFailureTests
{
	[Fact]
	public async Task UnrecordableSession_LoginFails()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Unrecorded", "password");
		app.SessionRecordFailure = new InvalidOperationException("the database refused the session");

		await Assert.ThrowsAsync<InvalidOperationException>(() => app.AttemptLoginAsync("Unrecorded", "password"));

		Assert.DoesNotContain(user, app.Users.Online.Keys);
	}
}
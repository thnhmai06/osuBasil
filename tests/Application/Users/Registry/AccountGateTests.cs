using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Registry;

/// <summary>Logins of one account are serialised and logins of different accounts do not disturb each other.</summary>
public sealed class AccountGateTests
{
	[Fact]
	public async Task TwoAccounts_BothAccepted()
	{
		using var app = new TestApplication();
		var first = await app.CreateUserAsync("GateFirst", "password");
		var second = await app.CreateUserAsync("GateSecond", "password");

		var results = await Task.WhenAll(
			Task.Run(() => app.AttemptLoginAsync(first.Value.Name, "password", SessionType.Stable),
				TestContext.Current.CancellationToken),
			Task.Run(() => app.AttemptLoginAsync(second.Value.Name, "password", SessionType.Stable),
				TestContext.Current.CancellationToken));

		Assert.All(results, result => Assert.NotNull(result.Connection));
	}

	[Fact]
	public async Task ManyLogins_OneStableEach()
	{
		using var app = new TestApplication();
		var first = await app.CreateUserAsync("GateOne", "password");
		var second = await app.CreateUserAsync("GateTwo", "password");

		var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
			Task.Run(() => app.AttemptLoginAsync((i % 2 == 0 ? first : second).Value.Name, "password",
				SessionType.Stable), TestContext.Current.CancellationToken)));

		Assert.Equal(2, results.Count(result => result.Connection is not null));
		Assert.Single(app.Users.Of(first)[SessionType.Stable]);
		Assert.Single(app.Users.Of(second)[SessionType.Stable]);
	}
}

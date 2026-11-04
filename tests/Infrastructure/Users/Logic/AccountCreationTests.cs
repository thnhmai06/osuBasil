using Basil.Application.Authentication.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Common;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Users.Tests.Logic;

/// <summary>An account is created whole: its credential and its record are stored, or neither.</summary>
public sealed class AccountCreationTests
{
	[Fact]
	public async Task RegisterAsync_WhenTheCredentialCannotBeStored_ThrowsAndLeavesNoAccountBehind()
	{
		using var app = new TestApplication(services =>
			services.AddSingleton<ICredentialRepository>(new RefusingCredentials()));
		var attempt = new RegisterAttempt("Unfinished", new Md5("0123456789abcdef0123456789abcdef"));

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			app.Users.RegisterAsync(attempt, TestContext.Current.CancellationToken));

		Assert.Null(await app.Provider.GetRequiredService<IUserRepository>()
			.GetByNameAsync("Unfinished", TestContext.Current.CancellationToken));
	}

	private sealed class RefusingCredentials : ICredentialRepository
	{
		public Task<bool> VerifyAsync(Credentials credentials, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(false);
		}

		public Task CreateOrUpdateAsync(Credentials credentials, CancellationToken cancellationToken = default)
		{
			throw new InvalidOperationException("the database refused the credential");
		}

		public Task DeleteAsync(UserRecord user, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}
	}
}

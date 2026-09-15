using Basil.Domain.Auth;
using Basil.Domain.Users;
using NSubstitute;

namespace Basil.Domain.Tests;

/// <summary>Verifies `CredentialVerifier`'s password-hash comparison.</summary>
public class CredentialVerifierTests
{
	private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
	private readonly IUserRepository _users = Substitute.For<IUserRepository>();

	private CredentialVerifier MakeVerifier()
	{
		return new CredentialVerifier(_users, _passwordHasher);
	}

	[Fact]
	public async Task NoStoredPasswordHash_ReturnsFalse()
	{
		_users.FetchPasswordHashAsync(1, Arg.Any<CancellationToken>()).Returns((string?)null);

		var result = await MakeVerifier().VerifyPasswordAsync(1, "hash");

		Assert.False(result);
	}

	[Fact]
	public async Task WrongPassword_ReturnsFalse()
	{
		_users.FetchPasswordHashAsync(1, Arg.Any<CancellationToken>()).Returns("stored-hash");
		_passwordHasher.Verify(Arg.Any<byte[]>(), "stored-hash").Returns(false);

		var result = await MakeVerifier().VerifyPasswordAsync(1, "wrong-md5");

		Assert.False(result);
	}

	[Fact]
	public async Task CorrectPassword_ReturnsTrue()
	{
		_users.FetchPasswordHashAsync(1, Arg.Any<CancellationToken>()).Returns("stored-hash");
		_passwordHasher.Verify(Arg.Any<byte[]>(), "stored-hash").Returns(true);

		var result = await MakeVerifier().VerifyPasswordAsync(1, "correct-md5");

		Assert.True(result);
	}
}

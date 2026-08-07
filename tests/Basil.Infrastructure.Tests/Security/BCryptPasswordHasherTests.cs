using System.Security.Cryptography;
using System.Text;
using Basil.Infrastructure.Security;

namespace Basil.Infrastructure.Tests.Security;

/// <summary>
///     Verifies `BCryptPasswordHasher` hashes and verifies the md5 digest of the plaintext password
///     (the client sends md5(password), never the raw password).
/// </summary>
public class BCryptPasswordHasherTests
{
	// matches bancho.py: hashlib.md5(password.encode()).hexdigest().encode() — the 32-char lowercase
	// hex digest AS ASCII BYTES, not the raw 16-byte digest. This is what actually gets bcrypt-hashed.
	private static byte[] Md5(string password)
	{
		return Encoding.ASCII.GetBytes(
			Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(password))));
	}

	[Fact]
	public void Verify_CorrectPasswordMd5_ReturnsTrue()
	{
		var hasher = new BCryptPasswordHasher();
		var passwordMd5 = Md5("hunter2");
		var hash = hasher.Hash(passwordMd5);

		Assert.True(hasher.Verify(passwordMd5, hash));
	}

	[Fact]
	public void Verify_WrongPasswordMd5_ReturnsFalse()
	{
		var hasher = new BCryptPasswordHasher();
		var hash = hasher.Hash(Md5("hunter2"));

		Assert.False(hasher.Verify(Md5("wrong-password"), hash));
	}

	[Fact]
	public void Verify_SameHashTwice_UsesCacheAndStillReturnsCorrectResult()
	{
		var hasher = new BCryptPasswordHasher();
		var passwordMd5 = Md5("hunter2");
		var hash = hasher.Hash(passwordMd5);

		Assert.True(hasher.Verify(passwordMd5, hash));
		// second call should hit the in-memory cache path, not re-run bcrypt
		Assert.True(hasher.Verify(passwordMd5, hash));
		Assert.False(hasher.Verify(Md5("wrong-password"), hash));
	}
}
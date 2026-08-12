using System.Security.Cryptography;
using System.Text;

namespace Basil.LoadTests.Helpers;

/// <summary>Computes the lowercase hex MD5 digest the login and account-creation contracts require.</summary>
public static class Md5Hex
{
	/// <summary>Computes the lowercase hex MD5 of the UTF-8 bytes of <paramref name="value" />.</summary>
	/// <param name="value">The string to hash.</param>
	/// <returns>A 32-character lowercase hex string.</returns>
	public static string Of(string value)
	{
		return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));
	}
}
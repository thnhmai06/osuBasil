using System.Text;
using Basil.Application.Abstractions.Scores;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;

namespace Basil.Infrastructure.Security;

/// <inheritdoc cref="IScoreDecryptor" />
/// <remarks>
///     Uses 256-bit Rijndael in CBC mode with PKCS7 padding, matching the osu! client's cipher.
///     The key is the UTF-8 bytes of <c>osu!-scoreburgr---------</c> plus the submitting client's
///     osu! version string. The same key and IV decrypt both the score data and the client hash,
///     and the decrypted score data is split on <c>:</c> into its fields before being returned.
/// </remarks>
public sealed class RijndaelScoreDecryptor : IScoreDecryptor
{
	/// <summary>The Rijndael block size in bits, fixed at 256 to match the client's cipher.</summary>
	private const int BlockSizeBits = 256;

	/// <inheritdoc />
	public (string[] ScoreDataFields, string ClientHash) Decrypt(
		string scoreDataBase64, string clientHashBase64, string ivBase64, string osuVersion)
	{
		var key = Encoding.UTF8.GetBytes($"osu!-scoreburgr---------{osuVersion}");
		var iv = Convert.FromBase64String(ivBase64);

		var scoreData = Decrypt(Convert.FromBase64String(scoreDataBase64), key, iv);
		var clientHash = Decrypt(Convert.FromBase64String(clientHashBase64), key, iv);

		return (Encoding.UTF8.GetString(scoreData).Split(':'), Encoding.UTF8.GetString(clientHash));
	}

	/// <summary>
	///     Decrypts a single ciphertext with the given key and IV using 256-bit Rijndael in CBC mode
	///     with PKCS7 padding.
	/// </summary>
	/// <param name="ciphertext">The bytes to decrypt.</param>
	/// <param name="key">The cipher key.</param>
	/// <param name="iv">The initialization vector.</param>
	/// <returns>The decrypted plaintext bytes, without padding.</returns>
	private static byte[] Decrypt(byte[] ciphertext, byte[] key, byte[] iv)
	{
		var cipher = new PaddedBufferedBlockCipher(
			new CbcBlockCipher(new RijndaelEngine(BlockSizeBits)),
			new Pkcs7Padding());
		cipher.Init(false, new ParametersWithIV(new KeyParameter(key), iv));

		var output = new byte[cipher.GetOutputSize(ciphertext.Length)];
		var length = cipher.ProcessBytes(ciphertext, 0, ciphertext.Length, output, 0);
		length += cipher.DoFinal(output, length);

		return output[..length];
	}
}
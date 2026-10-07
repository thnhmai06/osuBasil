using System.Text;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;

namespace Basil.Protocol.Bancho.Models.Scores;

/// <summary>The cipher the osu! client applies to the score string and client hash of a score submission.</summary>
public static class ScoreSubmissionCipher
{
	/// <summary>The cipher block size in bits, 256 as in the client.</summary>
	private const int BlockSizeBits = 256;

	/// <summary>Decrypts a field of a score submission.</summary>
	/// <param name="encryptedBase64">The base64 ciphertext as the client posted it.</param>
	/// <param name="ivBase64">The base64 initialization vector the client posted with it.</param>
	/// <param name="osuVersion">The client's osu! version string, which is part of the key.</param>
	/// <returns>The plaintext, UTF-8 decoded.</returns>
	/// <exception cref="FormatException">A base64 argument is malformed.</exception>
	/// <exception cref="Org.BouncyCastle.Crypto.InvalidCipherTextException">The ciphertext does not decrypt with this key and IV.</exception>
	public static string Decrypt(string encryptedBase64, string ivBase64, string osuVersion)
	{
		var key = Encoding.UTF8.GetBytes($"osu!-scoreburgr---------{osuVersion}");
		var iv = Convert.FromBase64String(ivBase64);
		var ciphertext = Convert.FromBase64String(encryptedBase64);

		var cipher = new PaddedBufferedBlockCipher(
			new CbcBlockCipher(new RijndaelEngine(BlockSizeBits)),
			new Pkcs7Padding());
		cipher.Init(false, new ParametersWithIV(new KeyParameter(key), iv));

		var output = new byte[cipher.GetOutputSize(ciphertext.Length)];
		var length = cipher.ProcessBytes(ciphertext, 0, ciphertext.Length, output, 0);
		length += cipher.DoFinal(output, length);

		return Encoding.UTF8.GetString(output, 0, length);
	}
}

using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;

namespace Basil.Protocol.Bancho.Wire.Security;

/// <summary>The cipher the osu! client applies to the score string and client hash of a score submission.</summary>
/// <remarks>
///     The key is the 24-byte constant <c>osu!-scoreburgr---------</c> followed by the client's osu! version, so its
///     length depends on the version string and must be 16, 24 or 32 bytes. The initialization vector is 32 bytes.
/// </remarks>
public static class ScoreSubmissionCipher
{
	/// <summary>The cipher block size in bits, 256 as in the client.</summary>
	private const int BlockSizeBits = 256;

	/// <summary>The initialization vector length in bytes, as in the client.</summary>
	private const int InitializationVectorBytes = 32;

	/// <summary>Decrypts a field of a score submission.</summary>
	/// <param name="encryptedBase64">The base64 ciphertext as the client posted it.</param>
	/// <param name="ivBase64">The base64 initialization vector the client posted with it.</param>
	/// <param name="osuVersion">The client's osu! version string, which is part of the key.</param>
	/// <returns>The plaintext, UTF-8 decoded.</returns>
	/// <exception cref="ArgumentException">
	///     The key, the 24-byte constant followed by <paramref name="osuVersion" />, is not 16, 24 or 32 bytes long, or
	///     <paramref name="ivBase64" /> does not decode to 32 bytes.
	/// </exception>
	/// <exception cref="FormatException">A base64 argument is malformed.</exception>
	/// <exception cref="InvalidCipherTextException">
	///     The ciphertext does not decrypt with this key and initialization vector, or its padding is invalid.
	/// </exception>
	/// <exception cref="DataLengthException">
	///     The ciphertext is empty or does not end on a cipher block boundary.
	/// </exception>
	public static string Decrypt(string encryptedBase64, string ivBase64, string osuVersion)
	{
		var key = Encoding.UTF8.GetBytes($"osu!-scoreburgr---------{osuVersion}");
		if (key.Length is not (16 or 24 or 32))
			throw new ArgumentException(
				$"The cipher key must be 16, 24 or 32 bytes long, but it is {key.Length} bytes long.",
				nameof(osuVersion));

		var iv = Convert.FromBase64String(ivBase64);
		if (iv.Length != InitializationVectorBytes)
			throw new ArgumentException(
				$"The initialization vector must be {InitializationVectorBytes} bytes long, but it is {iv.Length} bytes long.",
				nameof(ivBase64));

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
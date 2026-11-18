using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;

namespace Basil.Protocol.Stable.Wire.Security;

public static class ScoreSubmissionCipher
{
	private const int BlockSizeBits = 256;

	private const int InitializationVectorBytes = 32;

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
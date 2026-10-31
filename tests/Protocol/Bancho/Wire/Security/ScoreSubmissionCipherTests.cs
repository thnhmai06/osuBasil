using System.Text;
using Basil.Protocol.Bancho.Wire.Security;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;

namespace Basil.Protocol.Bancho.Tests.Wire.Security;

/// <summary>
///     Verifies the score submission cipher contract: Rijndael-256 in CBC mode with PKCS#7 padding, a key
///     of the 24-byte constant <c>osu!-scoreburgr---------</c> followed by the osu! version (32 bytes with
///     an eight-character version, 24 bytes with none), a 32-byte initialization vector, and base64
///     inputs. Any prerequisite that trips throws the documented exception type.
/// </summary>
public class ScoreSubmissionCipherTests
{
	private static string ValidIvBase64 =>
		Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

	private static string ValidCiphertextBase64 => Encrypt("body", "20240815").Item1;

	public static TheoryData<string, string> RoundTripCases()
	{
		return new TheoryData<string, string>
		{
			// no version: a 24-byte key; ASCII body.
			{ "", "osu! score submission body" },
			// eight-character version: a 32-byte key; multibyte UTF-8 body.
			{ "20240815", "héllo 日本語" }
		};
	}

	public static TheoryData<string> InvalidKeyLengths()
	{
		// twenty-four constant bytes plus the version: 6 characters -> 30 bytes, 9 characters -> 33 bytes.
		return new TheoryData<string>
		{
			"abcdef",
			"abcdefghi"
		};
	}

	public static TheoryData<string, string> MalformedBase64()
	{
		// the ivBase64 length check runs first, so malformed-ciphertext rows pair it with a valid 32-byte vector.
		return new TheoryData<string, string>
		{
			{ "!!", ValidIvBase64 }, // malformed ciphertext
			{ ValidCiphertextBase64, "!!" } // malformed initialization vector
		};
	}

	[Theory]
	[MemberData(nameof(RoundTripCases))]
	public void Decrypt_CipherTextFromADocumentedKeyAndVector_RestoresThePlaintext(
		string osuVersion,
		string plaintext)
	{
		var (ciphertext, iv) = Encrypt(plaintext, osuVersion);

		var decrypted = ScoreSubmissionCipher.Decrypt(ciphertext, iv, osuVersion);

		Assert.Equal(plaintext, decrypted);
	}

	[Fact]
	public void Decrypt_InitializationVectorOfWrongLength_ThrowsArgumentException()
	{
		var (ciphertext, iv) = Encrypt("body", "20240815");
		var shortIv = Convert.ToBase64String(Convert.FromBase64String(iv)[..31]);

		Assert.Throws<ArgumentException>(() => ScoreSubmissionCipher.Decrypt(ciphertext, shortIv, "20240815"));
	}

	[Theory]
	[MemberData(nameof(InvalidKeyLengths))]
	public void Decrypt_KeyOfUndocumentedLength_ThrowsArgumentException(string osuVersion)
	{
		var key = Encoding.UTF8.GetBytes($"osu!-scoreburgr---------{osuVersion}");
		var iv = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
		var ciphertext = Convert.ToBase64String(new byte[32]);

		Assert.True(key.Length is not (16 or 24 or 32));
		Assert.Throws<ArgumentException>(() => ScoreSubmissionCipher.Decrypt(ciphertext, iv, osuVersion));
	}

	[Fact]
	public void Decrypt_CipherTextNotOnABlockBoundary_ThrowsDataLengthException()
	{
		var (ciphertextBase64, iv) = Encrypt("body", "20240815");
		var truncated = Convert.ToBase64String(Convert.FromBase64String(ciphertextBase64)[..5]);

		Assert.Throws<DataLengthException>(() => ScoreSubmissionCipher.Decrypt(truncated, iv, "20240815"));
	}

	[Fact]
	public void Decrypt_CipherTextWithCorruptedPadding_ThrowsInvalidCipherTextException()
	{
		// One exact block of CBC ciphertext whose final plaintext byte is 0x00, encrypted without padding.
		// PKCS#7 unpadding then sees a padding byte of zero and rejects the ciphertext deterministically.
		var plaintext = new byte[32];
		for (var i = 0; i < 31; i++) plaintext[i] = 0x41;
		plaintext[31] = 0x00;

		var key = Encoding.UTF8.GetBytes("osu!-scoreburgr---------20240815");
		var iv = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
		var cipher = new BufferedBlockCipher(new CbcBlockCipher(new RijndaelEngine(256)));
		cipher.Init(true, new ParametersWithIV(new KeyParameter(key), iv));
		var output = new byte[cipher.GetOutputSize(32)];
		var length = cipher.ProcessBytes(plaintext, 0, 32, output, 0);
		length += cipher.DoFinal(output, length);
		var ciphertext = Convert.ToBase64String(output, 0, length);

		Assert.Throws<InvalidCipherTextException>(() =>
			ScoreSubmissionCipher.Decrypt(ciphertext, Convert.ToBase64String(iv), "20240815"));
	}

	[Theory]
	[MemberData(nameof(MalformedBase64))]
	public void Decrypt_MalformedBase64Argument_ThrowsFormatException(string ciphertext, string iv)
	{
		Assert.Throws<FormatException>(() => ScoreSubmissionCipher.Decrypt(ciphertext, iv, "20240815"));
	}

	/// <summary>
	///     Produces a ciphertext per the client: Rijndael-256 in CBC mode with PKCS#7 padding over the UTF-8
	///     plaintext under the 24-byte constant plus <paramref name="osuVersion" />, with the fixed
	///     initialization vector <c>1..32</c>.
	/// </summary>
	private static (string CiphertextBase64, string IvBase64) Encrypt(string plaintext, string osuVersion)
	{
		var key = Encoding.UTF8.GetBytes($"osu!-scoreburgr---------{osuVersion}");
		var iv = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

		var cipher = new PaddedBufferedBlockCipher(
			new CbcBlockCipher(new RijndaelEngine(256)),
			new Pkcs7Padding());
		cipher.Init(true, new ParametersWithIV(new KeyParameter(key), iv));
		var input = Encoding.UTF8.GetBytes(plaintext);
		var output = new byte[cipher.GetOutputSize(input.Length)];
		var length = cipher.ProcessBytes(input, 0, input.Length, output, 0);
		length += cipher.DoFinal(output, length);

		return (Convert.ToBase64String(output, 0, length), Convert.ToBase64String(iv));
	}
}
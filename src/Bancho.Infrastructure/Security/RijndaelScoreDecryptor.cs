using System.Text;
using Bancho.Application.Abstractions.Scores;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;

namespace Bancho.Infrastructure.Security;

/// <summary>
///     Ported from app/encryption.py's decrypt_score_aes_data. This is genuinely Rijndael, not AES:
///     the osu! client uses a 256-bit block size (block_size=32 in the Python py3rijndael call),
///     which .NET's built-in Aes class cannot do — it's hardcoded to 128-bit blocks. BouncyCastle's
///     RijndaelEngine supports the configurable block size the protocol actually needs.
/// </summary>
public sealed class RijndaelScoreDecryptor : IScoreDecryptor
{
    private const int BlockSizeBits = 256;

    public (string[] ScoreDataFields, string ClientHash) Decrypt(
        string scoreDataBase64,
        string clientHashBase64,
        string ivBase64,
        string osuVersion)
    {
        var key = Encoding.UTF8.GetBytes($"osu!-scoreburgr---------{osuVersion}");
        var iv = Convert.FromBase64String(ivBase64);

        var scoreData = Decrypt(Convert.FromBase64String(scoreDataBase64), key, iv);
        var clientHash = Decrypt(Convert.FromBase64String(clientHashBase64), key, iv);

        return (Encoding.UTF8.GetString(scoreData).Split(':'), Encoding.UTF8.GetString(clientHash));
    }

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
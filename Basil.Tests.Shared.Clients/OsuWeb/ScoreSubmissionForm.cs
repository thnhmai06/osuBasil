using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Basil.Tests.Shared.Clients.Stable;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;

namespace Basil.Tests.Shared.Clients.OsuWeb;

/// <summary>Builds the pieces of a score submission the way the osu! client does: the checksum, the score line, its encryption and the multipart form.</summary>
public static class ScoreSubmissionForm
{
	private const int BlockSizeBits = 256;

	private const int InitializationVectorBytes = 32;

	/// <summary>Computes the score checksum the server recomputes and compares.</summary>
	/// <param name="score">The play.</param>
	/// <param name="identity">The identity that signed in; its name, build and client hash are part of the checksum.</param>
	public static string Checksum(PlayedScore score, StableIdentity identity)
	{
		var raw = string.Create(CultureInfo.InvariantCulture,
			$"chickenmcnuggets{score.Num100 + score.Num300}o15{score.Num50}{score.NumGeki}smustard{score.NumKatu}{score.NumMiss}uu{score.BeatmapMd5}{score.MaxCombo}{score.FullCombo}{identity.Name}{score.TotalScore}{score.Grade}{score.Mods}Q{score.Passed}{score.Mode}{identity.Version}{score.TimestampText}{identity.ClientHash}{score.StoryboardMd5}");
		return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(raw)));
	}

	/// <summary>Writes the colon-separated score line that gets encrypted.</summary>
	public static string Line(PlayedScore score, StableIdentity identity)
	{
		return string.Create(CultureInfo.InvariantCulture,
			$"{score.BeatmapMd5}:{identity.Name}:{Checksum(score, identity)}:{score.Num300}:{score.Num100}:{score.Num50}:{score.NumGeki}:{score.NumKatu}:{score.NumMiss}:{score.TotalScore}:{score.MaxCombo}:{score.FullCombo}:{score.Grade}:{score.Mods}:{score.Passed}:{score.Mode}:{score.TimestampText}:{identity.Version}");
	}

	/// <summary>Encrypts a text the way the client does, so the server's decryption reads it back.</summary>
	/// <param name="plainText">The text to hide.</param>
	/// <param name="iv">The 32-byte initialization vector, sent beside it.</param>
	/// <param name="osuVersion">The build date as eight digits; it is part of the key.</param>
	/// <returns>The encrypted text in base 64.</returns>
	public static string Encrypt(string plainText, byte[] iv, string osuVersion)
	{
		if (iv.Length != InitializationVectorBytes)
			throw new ArgumentException($"The initialization vector must be {InitializationVectorBytes} bytes.",
				nameof(iv));

		var cipher = new PaddedBufferedBlockCipher(new CbcBlockCipher(new RijndaelEngine(BlockSizeBits)),
			new Pkcs7Padding());
		cipher.Init(true,
			new ParametersWithIV(new KeyParameter(Encoding.UTF8.GetBytes($"osu!-scoreburgr---------{osuVersion}")), iv));
		var input = Encoding.UTF8.GetBytes(plainText);
		var output = new byte[cipher.GetOutputSize(input.Length)];
		var length = cipher.ProcessBytes(input, 0, input.Length, output, 0);
		length += cipher.DoFinal(output, length);
		return Convert.ToBase64String(output, 0, length);
	}

	/// <summary>Builds the multipart form of a submission.</summary>
	/// <param name="score">The play.</param>
	/// <param name="identity">The identity that signed in.</param>
	/// <param name="replay">The replay file the form carries.</param>
	public static MultipartFormDataContent Build(PlayedScore score, StableIdentity identity, byte[] replay)
	{
		var iv = RandomNumberGenerator.GetBytes(InitializationVectorBytes);
		var form = new MultipartFormDataContent
		{
			{ new StringContent(Encrypt(Line(score, identity), iv, identity.Version)), "score" },
			{ new StringContent(Convert.ToBase64String(iv)), "iv" },
			{ new StringContent(identity.Version), "osuver" },
			{ new StringContent(identity.PasswordMd5), "pass" },
			{ new StringContent(Encrypt(identity.ClientHash, iv, identity.Version)), "s" },
			{ new StringContent(identity.ClientSerial), "c1" }
		};
		if (score.StoryboardMd5 is not null) form.Add(new StringContent(score.StoryboardMd5), "sbk");
		form.Add(new ByteArrayContent(replay), "score", "replay");
		return form;
	}
}

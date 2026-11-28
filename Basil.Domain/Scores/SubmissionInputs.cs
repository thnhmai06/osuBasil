using Basil.Domain.Client;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Scores;

/// <summary>The hashes of the beatmap a play was made on, as the server knows them.</summary>
/// <param name="Hash">The hash of the beatmap file.</param>
/// <param name="StoryboardHash">The hash of the beatmap's storyboard, or <see langword="null" /> when it has none.</param>
public sealed record BeatmapChecksums(Md5Hash Hash, Md5Hash? StoryboardHash);

/// <summary>What the submitting client reported about itself and the beatmap it played.</summary>
/// <param name="ClientHash">The client hash text the client sent.</param>
/// <param name="Fingerprint">The fingerprint the client sent.</param>
/// <param name="UninstallId">The uninstall identifier the client sent.</param>
/// <param name="DiskSignature">The disk signature the client sent.</param>
/// <param name="VersionDate">The client version as a date, <c>yyyyMMdd</c>.</param>
/// <param name="BeatmapHash">The hash of the beatmap the client says it played.</param>
public sealed record SubmittedClient(
	string ClientHash,
	ClientFingerprint Fingerprint,
	string UninstallId,
	string DiskSignature,
	string VersionDate,
	Md5Hash BeatmapHash);

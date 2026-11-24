using Basil.Domain.Client;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Scores.Persistence;

public sealed record BeatmapChecksums(Md5Hash Hash, Md5Hash? StoryboardHash);

public sealed record SubmittedClient(
	string ClientHash,
	ClientFingerprint Fingerprint,
	string UninstallId,
	string DiskSignature,
	string VersionDate,
	Md5Hash BeatmapHash);
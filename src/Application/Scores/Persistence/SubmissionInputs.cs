using Basil.Domain.Client;
using Basil.Domain.Cryptography;

namespace Basil.Application.Scores.Persistence;

public sealed record BeatmapChecksums(Md5 Hash, Md5? StoryboardHash);

public sealed record SubmittedClient(
	string ClientHash,
	OsuFingerprint Fingerprint,
	string UninstallId,
	string DiskSignature,
	string VersionDate,
	Md5 BeatmapHash);
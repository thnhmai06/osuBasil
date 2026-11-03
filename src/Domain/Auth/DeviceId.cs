using Basil.Domain.Common;

namespace Basil.Domain.Auth;

/// <summary>The hashes by which an osu! device is recognised.</summary>
/// <param name="UninstallHash">The uninstall id hash the client reported.</param>
/// <param name="DiskSignatureHash">The disk signature hash the client reported.</param>
public readonly record struct DeviceId(Md5 UninstallHash, Md5 DiskSignatureHash);
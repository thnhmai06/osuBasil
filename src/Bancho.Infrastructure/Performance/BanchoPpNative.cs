using System.Runtime.InteropServices;

namespace Bancho.Infrastructure.Performance;

/// <summary>Raw P/Invoke surface for native/bancho-pp-ffi (see that crate for the Rust side).</summary>
internal static partial class BanchoPpNative
{
    private const string LibraryName = "bancho_pp_ffi";

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int bancho_pp_calculate_difficulty(string beatmapPath, uint mods, out double outStars);
}
namespace Basil.Infrastructure.Persistence.Tests.Support;

/// <summary>File names a storage must refuse as "not a safe file name": they escape the directory, or Windows or the stored reference cannot keep them as they are.</summary>
internal static class UnsafeNames
{
	/// <summary>Gets names that leave the storage's directory or name no file.</summary>
	public static TheoryData<string> Escaping { get; } =
	[
		"", "  ", "..", "../escape.png", "..\\escape.png", "folder/../../escape.png", "/rooted.png",
		"\\\\server\\share\\x.png"
	];

	/// <summary>Gets names Windows keeps for devices; no stored file may take one, whatever its extension.</summary>
	public static TheoryData<string> Devices { get; } =
	[
		"CON", "con.png", "NUL.txt", "PRN", "AUX.png", "COM1", "LPT1.png", "folder/COM9.png"
	];

	/// <summary>Gets names whose asset reference would not be canonical: a trailing dot, a colon, or a character no file name may have.</summary>
	public static TheoryData<string> NotCanonical { get; } =
	[
		"image.", "a:b.png", "x?y.png", "x*y.png", "x|y.png", "x<y.png"
	];
}
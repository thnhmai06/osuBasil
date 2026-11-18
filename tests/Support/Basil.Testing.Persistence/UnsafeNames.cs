namespace Basil.Testing.Persistence;

public static class UnsafeNames
{
	public static TheoryData<string> Escaping { get; } =
	[
		"", "  ", "..", "../escape.png", "..\\escape.png", "folder/../../escape.png", "/rooted.png",
		@"\\server\share\x.png"
	];

	public static TheoryData<string> Devices { get; } =
	[
		"CON", "con.png", "NUL.txt", "PRN", "AUX.png", "COM1", "LPT1.png", "folder/COM9.png"
	];

	public static TheoryData<string> NotCanonical { get; } =
	[
		"image.", "a:b.png", "x?y.png", "x*y.png", "x|y.png", "x<y.png"
	];
}
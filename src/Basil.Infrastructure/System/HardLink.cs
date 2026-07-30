using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Basil.Infrastructure.System;

/// <summary>Cross-platform hard-Link creation.</summary>
public static partial class HardLink
{
	/// <summary>
	///     Creates a hard Link at <paramref name="linkPath" /> pointing to the existing file at
	///     <paramref name="targetPath" />. Both paths must reside on the same volume.
	/// </summary>
	/// <param name="linkPath">Path where the hard Link will be created.</param>
	/// <param name="targetPath">Path to the existing file to Link to.</param>
	/// <param name="force">
	///     When <c>true</c> (default), deletes any existing file at <paramref name="linkPath" /> before creating
	///     the Link. When <c>false</c>, throws <see cref="IOException" /> if the path is already occupied.
	/// </param>
	/// <exception cref="IOException">The OS call failed or <paramref name="force" /> is false and the path exists.</exception>
	public static void Create(string linkPath, string targetPath, bool force = true)
	{
		if (force && File.Exists(linkPath)) File.Delete(linkPath);

		if (OperatingSystem.IsWindows())
			CreateWindows(linkPath, targetPath);
		else
			CreateUnix(linkPath, targetPath);
	}

	#region Windows

	[LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool CreateHardLinkW(string lpFileName, string lpExistingFileName,
		IntPtr lpSecurityAttributes);

	private static void CreateWindows(string linkPath, string targetPath)
	{
		if (!CreateHardLinkW(linkPath, targetPath, IntPtr.Zero))
			throw new IOException(
				$"CreateHardLinkW failed (Win32 error {Marshal.GetLastWin32Error()}): {linkPath} → {targetPath}");
	}

	#endregion

	#region Unix

	[LibraryImport("libc", EntryPoint = "link", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
	private static partial int Link(string oldPath, string newPath);

	private static void CreateUnix(string linkPath, string targetPath)
	{
		if (Link(targetPath, linkPath) != 0)
			throw new IOException(
				new Win32Exception(Marshal.GetLastWin32Error()).Message +
				$" Link({targetPath}, {linkPath})");
	}

	#endregion
}
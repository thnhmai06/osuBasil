using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Basil.Infrastructure.System;

/// <summary>Creates hard links on Windows and Unix.</summary>
/// <remarks>
///     Calls the Win32 <c>CreateHardLinkW</c> API on Windows and libc's <c>link</c> function
///     elsewhere, throwing <see cref="IOException" /> when the underlying call fails.
/// </remarks>
public static partial class HardLink
{
	/// <summary>
	///     Creates a hard link at <paramref name="linkPath" /> pointing to the existing file at
	///     <paramref name="targetPath" />. Both paths must reside on the same volume.
	/// </summary>
	/// <param name="linkPath">Path where the hard link will be created.</param>
	/// <param name="targetPath">Path to the existing file to link to.</param>
	/// <param name="force">
	///     <see langword="true" /> to delete any existing file at <paramref name="linkPath" /> before creating
	///     the link (the default); <see langword="false" /> to throw <see cref="IOException" /> when the path is
	///     already occupied.
	/// </param>
	/// <exception cref="IOException">The OS call failed, or <paramref name="force" /> is false and the path exists.</exception>
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

	/// <summary>Creates a hard link through the Win32 <c>CreateHardLinkW</c> API.</summary>
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

	/// <summary>Creates a hard link through libc's <c>link</c> function.</summary>
	private static void CreateUnix(string linkPath, string targetPath)
	{
		if (Link(targetPath, linkPath) != 0)
			throw new IOException(
				new Win32Exception(Marshal.GetLastWin32Error()).Message +
				$" Link({targetPath}, {linkPath})");
	}

	#endregion
}
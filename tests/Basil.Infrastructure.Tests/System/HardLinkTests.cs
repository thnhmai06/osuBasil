using Basil.Infrastructure.System;

namespace Basil.Infrastructure.Tests.System;

public class HardLinkTests
{
	[Fact]
	public void Create_LinkReflectsWritesToTarget()
	{
		var targetPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.target");
		var linkPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.link");
		try
		{
			File.WriteAllText(targetPath, "first line\n");
			HardLink.Create(linkPath, targetPath);

			File.AppendAllText(targetPath, "second line\n");

			var viaLink = File.ReadAllText(linkPath);
			Assert.Contains("second line", viaLink);
		}
		finally
		{
			File.Delete(targetPath);
			File.Delete(linkPath);
		}
	}

	[Fact]
	public void Create_WithExistingLinkPath_OverwritesByDefault()
	{
		var targetA = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.a");
		var targetB = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.b");
		var linkPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.link");
		try
		{
			File.WriteAllText(targetA, "content A");
			File.WriteAllText(targetB, "content B");

			HardLink.Create(linkPath, targetA);
			Assert.Equal("content A", File.ReadAllText(linkPath));

			HardLink.Create(linkPath, targetB);
			Assert.Equal("content B", File.ReadAllText(linkPath));
		}
		finally
		{
			File.Delete(targetA);
			File.Delete(targetB);
			File.Delete(linkPath);
		}
	}
}
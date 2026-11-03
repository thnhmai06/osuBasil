using Basil.Host.Logging;

namespace Basil.Host.Tests.Logging;

/// <summary>Sizes in the settings are written with the usual units, in powers of 1024.</summary>
public sealed class ByteSizeTests
{
	[Theory]
	[InlineData("100MB", 100L * 1024 * 1024)]
	[InlineData("1GB", 1024L * 1024 * 1024)]
	[InlineData("512KB", 512L * 1024)]
	[InlineData("2048", 2048)]
	[InlineData("2048B", 2048)]
	[InlineData("100 mb", 100L * 1024 * 1024)]
	[InlineData("0", 0)]
	[InlineData("", 0)]
	[InlineData(null, 0)]
	public void ASizeIsReadInBytes(string? text, long bytes)
	{
		Assert.Equal(bytes, ByteSize.Parse(text, "Setting"));
	}

	[Theory]
	[InlineData("MB")]
	[InlineData("12TB")]
	[InlineData("ten")]
	[InlineData("-5MB")]
	public void ATextThatIsNotASizeIsRefusedWithTheNameOfTheSetting(string text)
	{
		var exception = Assert.Throws<FormatException>(() => ByteSize.Parse(text, "Logging:File:Rolling:MaxSize"));

		Assert.Contains("Logging:File:Rolling:MaxSize", exception.Message);
	}
}
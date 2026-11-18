using Basil.Application.Bot.Replies;

namespace Basil.Application.Bot.Tests.Replies;

public sealed class RepliesOptionsTests
{
	[Fact]
	public void ByDefault_TemplatesFolder()
	{
		Assert.Equal("Configurations/Templates/replies.json", new RepliesOptions().Path);
	}
}
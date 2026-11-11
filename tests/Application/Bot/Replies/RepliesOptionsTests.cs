using Basil.Application.Bot.Replies;

namespace Basil.Application.Bot.Tests.Replies;

/// <summary>Where the operator-editable templates of BasilBot are looked for.</summary>
public sealed class RepliesOptionsTests
{
	[Fact]
	public void ByDefault_TemplatesFolder()
	{
		Assert.Equal("Configurations/Templates/replies.json", new RepliesOptions().Path);
	}
}
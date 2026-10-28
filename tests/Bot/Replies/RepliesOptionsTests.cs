using Basil.Bot.Replies;

namespace Basil.Bot.Tests.Replies;

/// <summary>Where the operator-editable templates of BasilBot are looked for.</summary>
public sealed class RepliesOptionsTests
{
	[Fact]
	public void Path_ByDefault_IsTheTemplatesFolderBesideTheProgram()
	{
		Assert.Equal("Configurations/Templates/replies.json", new RepliesOptions().Path);
	}
}

using Basil.Domain.Users;

namespace Basil.Domain.Tests.Users;

public class BasilBotTests
{
	[Theory]
	[InlineData(BasilBot.Id, true)]
	[InlineData(2, false)]
	[InlineData(1000, false)]
	public void IsFriendOfEveryone_HoldsForTheBotOnly(int id, bool expected)
	{
		var user = new UserRecord { Id = id, Value = new UserData { Name = "someone" } };

		Assert.Equal(expected, user.IsFriendOfEveryone());
	}
}
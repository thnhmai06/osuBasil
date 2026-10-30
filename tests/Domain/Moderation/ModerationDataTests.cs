using Basil.Domain.Moderation;
using Basil.Domain.Users;

namespace Basil.Domain.Tests.Moderation;

/// <summary>Verifies the validation of a recorded moderation action.</summary>
public class ModerationDataTests
{
	private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

	[Fact]
	public void Action_Undefined_Throws()
	{
		var user = new UserRecord { Id = 1, Value = new UserData { Name = "moderator" } };

		Assert.Throws<ArgumentOutOfRangeException>(() => new ModerationData
		{
			Action = (ModerationAction)200,
			By = user,
			Target = user,
			Timestamp = Now
		});
	}

	[Fact]
	public void Reason_AtTheLimit_IsAccepted()
	{
		var reason = new string('a', 2048);
		var data = MakeData(reason);

		Assert.Equal(2048, data.Reason!.Length);
	}

	[Fact]
	public void Reason_AboveTheLimit_Throws()
	{
		Assert.Throws<ArgumentException>(() => MakeData(new string('a', 2049)));
	}

	private static ModerationData MakeData(string? reason)
	{
		var user = new UserRecord { Id = 1, Value = new UserData { Name = "moderator" } };
		return new ModerationData
		{
			Action = ModerationAction.Note,
			By = user,
			Target = user,
			Timestamp = Now,
			Reason = reason
		};
	}
}
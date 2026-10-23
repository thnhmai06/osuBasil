using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Domain.Tests.Permissions;

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
		var data = MakeData();

		data.Reason = new string('a', 2048);

		Assert.Equal(2048, data.Reason!.Length);
	}

	[Fact]
	public void Reason_AboveTheLimit_Throws()
	{
		var data = MakeData();

		Assert.Throws<ArgumentException>(() => data.Reason = new string('a', 2049));
	}

	private static ModerationData MakeData()
	{
		var user = new UserRecord { Id = 1, Value = new UserData { Name = "moderator" } };
		return new ModerationData
		{
			Action = ModerationAction.Note,
			By = user,
			Target = user,
			Timestamp = Now
		};
	}
}
using System.Net;
using Basil.Domain.Authentication;

namespace Basil.Domain.Tests.Authentication;

public class ConnectionRecordTests
{
	private static readonly DateTimeOffset Start = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

	private static ConnectionData Data(long sessionId = 1, SessionEndReason? reason = null)
	{
		return new ConnectionData(sessionId, IPAddress.Loopback, null, Start, Start, null, reason);
	}

	[Fact]
	public void Equals_SameId_IsTrueWhateverTheDataIs()
	{
		var first = new ConnectionRecord { Id = 3, Value = Data() };
		var other = new ConnectionRecord { Id = 3, Value = Data(9) };

		Assert.Equal(first, other);
		Assert.Equal(first.GetHashCode(), other.GetHashCode());
	}

	[Fact]
	public void Equals_DifferentId_IsFalse()
	{
		Assert.NotEqual(new ConnectionRecord { Id = 1, Value = Data() },
			new ConnectionRecord { Id = 2, Value = Data() });
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void New_NonPositiveId_Throws(long id)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new ConnectionRecord { Id = id, Value = Data() });
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-5)]
	public void Data_NonPositiveSessionId_Throws(long sessionId)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Data(sessionId));
	}

	[Fact]
	public void Data_UndefinedCloseReason_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Data(reason: (SessionEndReason)200));
	}

	[Fact]
	public void Data_NoCloseReason_MeansTheConnectionIsOpen()
	{
		Assert.Null(Data().CloseReason);
		Assert.Null(Data().ClosedAt);
	}

	[Fact]
	public void ServerStopped_IsADefinedReason()
	{
		Assert.True(Enum.IsDefined(SessionEndReason.ServerStopped));
		Assert.Equal(SessionEndReason.ServerStopped, Data(reason: SessionEndReason.ServerStopped).CloseReason);
	}
}

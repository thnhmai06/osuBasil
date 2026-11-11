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
	public void SameId_TrueWhateverData()
	{
		var first = new ConnectionRecord { Id = 3, Value = Data() };
		var other = new ConnectionRecord { Id = 3, Value = Data(9) };

		Assert.Equal(first, other);
		Assert.Equal(first.GetHashCode(), other.GetHashCode());
	}

	[Fact]
	public void DifferentId_IsFalse()
	{
		Assert.NotEqual(new ConnectionRecord { Id = 1, Value = Data() },
			new ConnectionRecord { Id = 2, Value = Data() });
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void NonPositiveId_Throws(long id)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new ConnectionRecord { Id = id, Value = Data() });
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-5)]
	public void NonPositiveSession_Throws(long sessionId)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Data(sessionId));
	}

	[Fact]
	public void UndefinedCloseReason_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Data(reason: (SessionEndReason)200));
	}

	[Fact]
	public void NoCloseReason_MeansOpen()
	{
		Assert.Null(Data().CloseReason);
		Assert.Null(Data().ClosedAt);
	}

	[Fact]
	public void ServerStopped_DefinedReason()
	{
		Assert.True(Enum.IsDefined(SessionEndReason.ServerStopped));
		Assert.Equal(SessionEndReason.ServerStopped, Data(reason: SessionEndReason.ServerStopped).CloseReason);
	}
}

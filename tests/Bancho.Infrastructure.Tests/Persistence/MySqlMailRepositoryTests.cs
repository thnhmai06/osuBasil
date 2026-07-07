using Bancho.Infrastructure.Persistence.Repositories;
namespace Bancho.Infrastructure.Tests.Persistence;

/// <summary>Ported from app/repositories/mail.py, scoped to what login needs: unread mail delivery.</summary>
public class MySqlMailRepositoryTests : IClassFixture<MySqlFixture>
{
    private readonly Bancho.Infrastructure.Persistence.Repositories.MySqlMailRepository _repository;
    private readonly Bancho.Infrastructure.Persistence.Repositories.MySqlUserRepository _users;

    public MySqlMailRepositoryTests(MySqlFixture fixture)
    {
        _repository = new Bancho.Infrastructure.Persistence.Repositories.MySqlMailRepository(fixture.ConnectionString);
        _users = new Bancho.Infrastructure.Persistence.Repositories.MySqlUserRepository(fixture.ConnectionString);
    }

    [Fact]
    public async Task Create_ThenFetchUnread_ReturnsMailWithUsernames()
    {
        var recipient = await _users.CreateAsync("mail recipient", "mail-recipient@example.test", "hash", "xx");

        await _repository.CreateAsync(fromId: 1, toId: recipient.Id, msg: "hello there");

        var unread = await _repository.FetchUnreadMailToUserAsync(recipient.Id);

        Assert.Single(unread);
        Assert.Equal("hello there", unread[0].Msg);
        Assert.Equal("BanchoBot", unread[0].FromName);
        Assert.False(unread[0].Read);
    }

    [Fact]
    public async Task MarkConversationAsRead_ExcludesFromFutureUnreadFetch()
    {
        var recipient = await _users.CreateAsync("mail recipient 2", "mail-recipient2@example.test", "hash", "xx");
        await _repository.CreateAsync(fromId: 1, toId: recipient.Id, msg: "msg one");

        await _repository.MarkConversationAsReadAsync(toId: recipient.Id, fromId: 1);

        var unread = await _repository.FetchUnreadMailToUserAsync(recipient.Id);
        Assert.Empty(unread);
    }

    [Fact]
    public async Task FetchUnread_NoMail_ReturnsEmpty()
    {
        var recipient = await _users.CreateAsync("mail recipient 3", "mail-recipient3@example.test", "hash", "xx");

        Assert.Empty(await _repository.FetchUnreadMailToUserAsync(recipient.Id));
    }
}

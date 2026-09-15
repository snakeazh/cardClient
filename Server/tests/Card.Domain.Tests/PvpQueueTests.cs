using CardShare.Contracts;
using CardShare.Domain;
using CardShare.Domain.Players;
using CardShare.Domain.Pvp;
using CardShare.Domain.Services;
using CardShare.Infrastructure.Memory;
using Xunit;

namespace CardShare.Domain.Tests;

public class AvatarUrlPolicyTests
{
    [Theory]
    [InlineData("https://thirdwx.qlogo.cn/mmopen/abc")]
    [InlineData("https://wx.qlogo.cn/mmhead/ver_1/xyz")]
    [InlineData("https://p3.douyinpic.com/aweme/100x100/foo.jpeg")]
    [InlineData("https://p16-sign-va.tiktokcdn.com/tos-maliva/img.png")]
    [InlineData("https://sf3-cdn-tos.byteimg.com/obj/avatar")]
    public void AcceptsHttpsWhitelistHosts(string url)
    {
        Assert.True(AvatarUrlPolicy.TryNormalize(url, out var normalized));
        Assert.StartsWith("https://", normalized, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://thirdwx.qlogo.cn/mmopen/abc")]
    [InlineData("https://evil.example/avatar.png")]
    [InlineData("ftp://wx.qlogo.cn/a")]
    [InlineData("not-a-url")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsHttpAndUnknownHosts(string? url)
    {
        Assert.False(AvatarUrlPolicy.TryNormalize(url, out _));
    }

    [Fact]
    public void NickNameIsTrimmedAndCappedAt32()
    {
        Assert.Equal(string.Empty, AvatarUrlPolicy.NormalizeNickName("  "));
        Assert.Equal("张三", AvatarUrlPolicy.NormalizeNickName(" 张三 "));
        var longName = new string('啊', 40);
        Assert.Equal(32, AvatarUrlPolicy.NormalizeNickName(longName).Length);
    }
}

public class LoginUserInfoTests
{
    [Fact]
    public async Task LoginStoresWhitelistAvatarAndNick()
    {
        var auth = CreateAuth();
        var login = await auth.LoginAsync(new LoginRequest
        {
            Provider = "guest",
            Code = "device-a",
            UserInfo = new LoginUserInfo
            {
                NickName = " 张三 ",
                AvatarUrl = "https://thirdwx.qlogo.cn/mmopen/ok"
            }
        }, CancellationToken.None);

        Assert.Equal("张三", login.Profile.NickName);
        Assert.Equal("https://thirdwx.qlogo.cn/mmopen/ok", login.Profile.AvatarUrl);
    }

    [Fact]
    public async Task LoginIgnoresNonWhitelistUrlAndKeepsOldAvatar()
    {
        var auth = CreateAuth();
        await auth.LoginAsync(new LoginRequest
        {
            Provider = "guest",
            Code = "device-b",
            UserInfo = new LoginUserInfo
            {
                NickName = "李四",
                AvatarUrl = "https://wx.qlogo.cn/old"
            }
        }, CancellationToken.None);

        var again = await auth.LoginAsync(new LoginRequest
        {
            Provider = "guest",
            Code = "device-b",
            UserInfo = new LoginUserInfo
            {
                NickName = "李四",
                AvatarUrl = "https://evil.example/hack.png"
            }
        }, CancellationToken.None);

        Assert.Equal("李四", again.Profile.NickName);
        Assert.Equal("https://wx.qlogo.cn/old", again.Profile.AvatarUrl);
    }

    [Fact]
    public void ApplyUserInfoDoesNotClearNickWhenEmpty()
    {
        var profile = PlayerProfile.CreateNew(Guid.NewGuid(), TestConfig.Create(), DateTimeOffset.UtcNow);
        profile.NickName = "旧名";
        profile.AvatarUrl = "https://wx.qlogo.cn/keep";
        profile.ApplyUserInfo(new LoginUserInfo { NickName = "  ", AvatarUrl = "http://wx.qlogo.cn/no" });
        Assert.Equal("旧名", profile.NickName);
        Assert.Equal("https://wx.qlogo.cn/keep", profile.AvatarUrl);
    }

    private static AuthService CreateAuth()
    {
        var config = TestConfig.Create();
        var clock = new TestClock();
        return new AuthService(
            new ICodeSessionClient[] { new GuestClient() },
            new MemoryAuthBindingRepository(),
            new MemoryPlayerRepository(),
            new MemoryTokenService(clock, TimeSpan.FromHours(1), TimeSpan.FromDays(1)),
            config,
            clock,
            guestEnabled: true);
    }

    private sealed class GuestClient : ICodeSessionClient
    {
        public string Provider => AuthProviders.Guest;

        public Task<CodeSession> ExchangeAsync(string code, CancellationToken cancellationToken)
            => Task.FromResult(new CodeSession("guest:" + code));
    }
}

public class PvpMatchmakerTests
{
    [Fact]
    public void QueueBroadcastsRosterUntilFourThenOpensRoom()
    {
        var matchmaker = new InMemoryPvpMatchmaker();
        var one = Public("甲", "https://wx.qlogo.cn/a");
        var two = Public("乙", "https://wx.qlogo.cn/b");
        var three = Public("丙", "");
        var four = Public("丁", "https://p3.douyinpic.com/c");

        var first = matchmaker.Enqueue(one);
        Assert.False(first.RoomOpened);
        Assert.Single(first.Players);
        Assert.Equal(one.UserId, first.Players[0].UserId);
        Assert.Equal("甲", first.Players[0].NickName);
        Assert.Equal("https://wx.qlogo.cn/a", first.Players[0].AvatarUrl);
        Assert.Equal(first.Joiner, Guid.Parse(one.UserId));

        var second = matchmaker.Enqueue(two);
        Assert.False(second.RoomOpened);
        Assert.Equal(2, second.Players.Count);
        Assert.Equal(2, second.Recipients.Count);

        var third = matchmaker.Enqueue(three);
        Assert.False(third.RoomOpened);
        Assert.Equal(3, third.Players.Count);

        var ready = matchmaker.Enqueue(four);
        Assert.True(ready.RoomOpened);
        Assert.NotNull(ready.Room);
        Assert.Equal(4, ready.Players.Count);
        Assert.Equal(4, ready.Room!.Players.Count);
        Assert.Equal(4, ready.Recipients.Count);
        Assert.Contains(ready.Players, p => p.NickName == "丙" && p.AvatarUrl == "");
    }

    [Fact]
    public void CancelRemovesPlayerAndReturnsRemainingRoster()
    {
        var matchmaker = new InMemoryPvpMatchmaker();
        var one = Public("甲");
        var two = Public("乙");
        var three = Public("丙");
        matchmaker.Enqueue(one);
        matchmaker.Enqueue(two);
        matchmaker.Enqueue(three);

        var left = matchmaker.Cancel(Guid.Parse(two.UserId));
        Assert.NotNull(left);
        Assert.False(left!.RoomOpened);
        Assert.Equal(2, left.Players.Count);
        Assert.DoesNotContain(left.Players, p => p.UserId == two.UserId);
        Assert.Equal(2, left.Recipients.Count);
        Assert.Contains(Guid.Parse(one.UserId), left.Recipients);
        Assert.Contains(Guid.Parse(three.UserId), left.Recipients);
    }

    [Fact]
    public void FifthPlayerStartsANewQueueAfterRoomOpens()
    {
        var matchmaker = new InMemoryPvpMatchmaker();
        for (var i = 0; i < 4; i++)
        {
            var evt = matchmaker.Enqueue(Public("p" + i));
            if (i < 3)
            {
                Assert.False(evt.RoomOpened);
            }
            else
            {
                Assert.True(evt.RoomOpened);
            }
        }

        var fifth = matchmaker.Enqueue(Public("戊"));
        Assert.False(fifth.RoomOpened);
        Assert.Single(fifth.Players);
        Assert.Equal("戊", fifth.Players[0].NickName);
    }

    [Fact]
    public void RequeueUsesLatestSnapshotAndDoesNotDuplicate()
    {
        var matchmaker = new InMemoryPvpMatchmaker();
        var userId = Guid.NewGuid();
        var first = matchmaker.Enqueue(new PlayerPublic
        {
            UserId = userId.ToString("N"),
            NickName = "旧",
            AvatarUrl = "https://wx.qlogo.cn/old"
        });
        Assert.False(first.RoomOpened);

        var again = matchmaker.Enqueue(new PlayerPublic
        {
            UserId = userId.ToString("N"),
            NickName = "新",
            AvatarUrl = "https://wx.qlogo.cn/new"
        });
        Assert.Single(again.Players);
        Assert.Equal("新", again.Players[0].NickName);
        Assert.Equal("https://wx.qlogo.cn/new", again.Players[0].AvatarUrl);
    }

    [Fact]
    public void ToPublicMapsProfileFields()
    {
        var profile = PlayerProfile.CreateNew(Guid.NewGuid(), TestConfig.Create(), DateTimeOffset.UtcNow);
        profile.NickName = "游客甲";
        profile.AvatarUrl = "https://wx.qlogo.cn/x";
        var pub = ProfileMapper.ToPublic(profile);
        Assert.Equal(profile.UserId.ToString("N"), pub.UserId);
        Assert.Equal("游客甲", pub.NickName);
        Assert.Equal("https://wx.qlogo.cn/x", pub.AvatarUrl);
    }

    private static PlayerPublic Public(string nick, string avatar = "")
    {
        return new PlayerPublic
        {
            UserId = Guid.NewGuid().ToString("N"),
            NickName = nick,
            AvatarUrl = avatar
        };
    }
}

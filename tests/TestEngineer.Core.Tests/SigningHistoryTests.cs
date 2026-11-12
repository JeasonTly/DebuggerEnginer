using TestEngineer.Core.Apk;

namespace TestEngineer.Core.Tests;

public sealed class SigningHistoryTests
{
    [Fact]
    public void Upsert_SamePathMovesToFrontAndKeepsId()
    {
        var older = new SigningProfile { Id = "keep", KeystorePath = @"D:\keys\App.jks", Alias = "old" };
        var other = new SigningProfile { Id = "other", KeystorePath = @"D:\keys\other.jks", Alias = "b" };
        var updated = new SigningProfile { KeystorePath = @"d:\keys\app.jks", Alias = "new", StorePasswordProtected = "cipher" };

        var list = SigningHistory.Upsert([other, older], updated);

        Assert.Equal(["keep", "other"], list.Select(item => item.Id));
        Assert.Equal("new", list[0].Alias);
        Assert.Equal("cipher", list[0].StorePasswordProtected);
    }

    [Fact]
    public void Upsert_DropsTheOldestPastTheLimit()
    {
        var current = Enumerable.Range(0, SigningHistory.Limit)
            .Select(index => new SigningProfile { Id = index.ToString(), KeystorePath = index + ".jks" })
            .ToList();

        var list = SigningHistory.Upsert(current, new SigningProfile { KeystorePath = "new.jks" }, limit: 3);

        Assert.Equal(["new.jks", "0.jks", "1.jks"], list.Select(item => item.KeystorePath));
    }
}

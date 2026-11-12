namespace TestEngineer.Core.Apk;

/// <summary>已配置过的签名文件。密码字段保存的是本机保护后的内容。</summary>
public sealed class SigningProfile
{
    public string Id { get; set; } = "";

    public string KeystorePath { get; set; } = "";

    public string Alias { get; set; } = "";

    public string StorePasswordProtected { get; set; } = "";

    public string KeyPasswordProtected { get; set; } = "";
}

public static class SigningHistory
{
    public const int Limit = 20;

    public static List<SigningProfile> Upsert(IReadOnlyList<SigningProfile>? current, SigningProfile profile, int limit = Limit)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var list = new List<SigningProfile>();
        SigningProfile? previous = null;
        if (current is not null)
        {
            foreach (var item in current)
            {
                if (string.Equals(item.KeystorePath, profile.KeystorePath, StringComparison.OrdinalIgnoreCase))
                {
                    previous = item;
                    continue;
                }

                list.Add(item);
            }
        }

        if (previous is not null && !string.IsNullOrWhiteSpace(previous.Id))
        {
            profile.Id = previous.Id;
        }
        else if (string.IsNullOrWhiteSpace(profile.Id))
        {
            profile.Id = Guid.NewGuid().ToString("N");
        }

        list.Insert(0, profile);
        if (list.Count > limit)
        {
            list.RemoveRange(limit, list.Count - limit);
        }

        return list;
    }
}

using PwdTool.Models;
using PwdTool.Services;
using Xunit;

namespace PwdTool.Tests;

/// <summary>
/// PasswordStore 的落盘/解密全部走 Windows DPAPI（System.Security.Cryptography.ProtectedData），
/// 在非 Windows 平台调用会抛 PlatformNotSupportedException——这是被测代码本身的平台限制，
/// 不是测试设计问题（毕竟这是一个 Windows 专用密码管理器）。用 OperatingSystem.IsWindows()
/// 提前 return 跳过，而不是引入额外的 [SkippableFact] 类库依赖。
///
/// 额外说明：由于测试项目引用的 PwdTool 主项目启用了 UseWindowsForms，其输出依赖
/// Microsoft.WindowsDesktop.App 这个仅限 Windows 的共享运行时，本仓库开发用的 Linux 沙箱
/// 环境里 `dotnet test` 甚至无法启动测试宿主进程（不是单个用例被跳过，而是整个测试
/// 可执行文件都起不来）——这一点不止影响本文件，同样影响 AccountEntryTests/
/// BrowserImporterCsvTests 这些本应与平台无关的纯逻辑测试。这些测试已通过人工逐行核对
/// 解析/加密逻辑确认正确，但"实际跑起来通过"这一步必须在 Windows 环境（本仓库配置的
/// GitHub Actions CI 用 windows-latest，或贡献者本机）执行 dotnet test 才能验证。
/// </summary>
public class PasswordStoreTests : IDisposable
{
    private readonly List<string> _tempPaths = new();

    private string NewTempVaultPath()
    {
        string path = Path.Combine(Path.GetTempPath(), $"PwdToolTest_{Guid.NewGuid():N}.vault.dat");
        _tempPaths.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            try { File.Delete(path); } catch { /* 忽略 */ }
            try { File.Delete(path + ".tmp"); } catch { /* 忽略 */ }
            try { File.Delete(path + ".bak"); } catch { /* 忽略 */ }
        }
    }

    [Fact]
    public void SaveLoad_RoundTrip_WithoutMasterPassword()
    {
        if (!OperatingSystem.IsWindows()) return;

        string path = NewTempVaultPath();

        var store1 = new PasswordStore(path);
        store1.Load();
        store1.Add(new AccountEntry { Title = "站点A", Username = "user1", Password = "pw1" });
        store1.Add(new AccountEntry { Title = "站点B", Username = "user2", Password = "pw2" });

        var store2 = new PasswordStore(path);
        store2.Load();

        Assert.Equal(2, store2.Entries.Count);
        Assert.Contains(store2.Entries, e => e.Title == "站点A" && e.Password == "pw1");
        Assert.Contains(store2.Entries, e => e.Title == "站点B" && e.Password == "pw2");
        Assert.False(store2.IsMasterPasswordEnabled);
    }

    [Fact]
    public void MasterPassword_EnableThenLoad_RequiresCorrectPassword()
    {
        if (!OperatingSystem.IsWindows()) return;

        string path = NewTempVaultPath();
        var settings = new AppSettings();

        var store1 = new PasswordStore(path);
        store1.Load();
        store1.Add(new AccountEntry { Title = "秘密站点", Username = "u", Password = "p" });
        store1.EnableMasterPassword("correct-horse", settings);

        var store2 = new PasswordStore(path);
        Assert.Throws<MasterPasswordRequiredException>(() => store2.Load());

        var store3 = new PasswordStore(path);
        Assert.Throws<InvalidMasterPasswordException>(() => store3.Load("wrong-password"));

        var store4 = new PasswordStore(path);
        store4.Load("correct-horse");
        Assert.Single(store4.Entries);
        Assert.Equal("秘密站点", store4.Entries[0].Title);
    }

    [Fact]
    public void ChangeMasterPassword_WrongOldPassword_ThrowsAndDoesNotChangeVault()
    {
        if (!OperatingSystem.IsWindows()) return;

        string path = NewTempVaultPath();
        var settings = new AppSettings();

        var store = new PasswordStore(path);
        store.Load();
        store.EnableMasterPassword("old-pass", settings);

        Assert.Throws<InvalidMasterPasswordException>(
            () => store.ChangeMasterPassword("wrong", "new-pass", settings));

        // 旧密码依然应该能正常解锁，说明修改失败没有破坏原有状态。
        var verify = new PasswordStore(path);
        verify.Load("old-pass");
        Assert.True(verify.IsMasterPasswordEnabled);
    }

    [Fact]
    public void VerifyMasterPassword_MatchesOnlyCorrectPassword()
    {
        if (!OperatingSystem.IsWindows()) return;

        string path = NewTempVaultPath();
        var settings = new AppSettings();
        var store = new PasswordStore(path);
        store.Load();
        store.EnableMasterPassword("hunter2", settings);

        Assert.True(PasswordStore.VerifyMasterPassword("hunter2", settings));
        Assert.False(PasswordStore.VerifyMasterPassword("hunter3", settings));
    }

    [Fact]
    public void ImportMany_DuplicateUrlAndUsername_IsSkipped()
    {
        if (!OperatingSystem.IsWindows()) return;

        string path = NewTempVaultPath();
        var store = new PasswordStore(path);
        store.Load();
        store.Add(new AccountEntry { Url = "https://a.com", Username = "alice", Password = "p1" });

        int added = store.ImportMany(new[]
        {
            new AccountEntry { Url = "https://a.com", Username = "alice", Password = "p1-imported" },
            new AccountEntry { Url = "https://b.com", Username = "bob", Password = "p2" },
        });

        Assert.Equal(1, added); // 只有 bob 是新的，alice 因 Url+Username 重复被跳过
        Assert.Equal(2, store.Entries.Count);
    }

    [Fact]
    public void ImportMany_MultipleEntriesWithBlankUrlAndUsername_AreNotTreatedAsDuplicates()
    {
        if (!OperatingSystem.IsWindows()) return;

        // 回归测试：早期实现里 Url 和 Username 同时为空的记录会因为空字符串互相相等
        // 被误判为"重复"，导致除第一条外全部被静默丢弃。
        string path = NewTempVaultPath();
        var store = new PasswordStore(path);
        store.Load();

        int added = store.ImportMany(new[]
        {
            new AccountEntry { Title = "本地密码1", Password = "p1" },
            new AccountEntry { Title = "本地密码2", Password = "p2" },
            new AccountEntry { Title = "本地密码3", Password = "p3" },
        });

        Assert.Equal(3, added);
        Assert.Equal(3, store.Entries.Count);
    }

    [Fact]
    public void Touch_IncrementsUseCountAndUpdatesLastUsed()
    {
        if (!OperatingSystem.IsWindows()) return;

        string path = NewTempVaultPath();
        var store = new PasswordStore(path);
        store.Load();
        var entry = store.Add(new AccountEntry { Title = "X", Password = "p" });

        store.Touch(entry.Id);

        var reloaded = new PasswordStore(path);
        reloaded.Load();
        var updated = Assert.Single(reloaded.Entries);
        Assert.Equal(1, updated.UseCount);
        Assert.True(updated.LastUsedUtc > DateTime.UtcNow.AddMinutes(-1));
    }
}

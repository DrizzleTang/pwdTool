using PwdTool.Services;
using Xunit;

namespace PwdTool.Tests;

/// <summary>
/// BrowserImporter.ImportFromCsv 纯粹是文件 IO + 字符串解析，不涉及 DPAPI/SQLite/WinForms，
/// 在任何平台（含本仓库开发用的 Linux 沙箱）都能实际跑起来验证，不像 PasswordStoreTests
/// 那样需要真实 Windows 环境。
/// </summary>
public class BrowserImporterCsvTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private string WriteTempCsv(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"PwdToolTest_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try { File.Delete(path); } catch { /* 忽略 */ }
        }
    }

    [Fact]
    public void ImportFromCsv_StandardChromeExportHeader_ParsesAllRows()
    {
        string csv = "name,url,username,password\n" +
                     "GitHub,https://github.com,alice,pass1\n" +
                     "Example,https://example.com,bob,pass2\n";
        string path = WriteTempCsv(csv);

        var result = BrowserImporter.ImportFromCsv(path);

        Assert.Null(result.Error);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("GitHub", result.Entries[0].Title);
        Assert.Equal("alice", result.Entries[0].Username);
        Assert.Equal("pass1", result.Entries[0].Password);
        Assert.All(result.Entries, e => Assert.Equal("CSV导入", e.Source));
    }

    [Fact]
    public void ImportFromCsv_ChineseHeader_ParsesCorrectly()
    {
        string csv = "标题,网址,用户名,密码\n示例站点,https://a.com,张三,mypassword\n";
        string path = WriteTempCsv(csv);

        var result = BrowserImporter.ImportFromCsv(path);

        Assert.Null(result.Error);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("示例站点", entry.Title);
        Assert.Equal("张三", entry.Username);
        Assert.Equal("mypassword", entry.Password);
    }

    [Fact]
    public void ImportFromCsv_QuotedFieldWithEmbeddedCommaAndNewline_ParsesCorrectly()
    {
        // RFC4180: 双引号包裹的字段内可以含逗号和换行，双引号本身用两个双引号转义。
        string csv = "name,url,username,password\n" +
                     "\"Site, Inc.\",https://site.example,\"user\"\"quote\"\"\",\"pa\nss\"\n";
        string path = WriteTempCsv(csv);

        var result = BrowserImporter.ImportFromCsv(path);

        Assert.Null(result.Error);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("Site, Inc.", entry.Title);
        Assert.Equal("user\"quote\"", entry.Username);
        Assert.Equal("pa\nss", entry.Password);
    }

    [Fact]
    public void ImportFromCsv_MissingPasswordColumn_ReturnsError()
    {
        string csv = "name,url,username\nSite,https://a.com,user\n";
        string path = WriteTempCsv(csv);

        var result = BrowserImporter.ImportFromCsv(path);

        Assert.NotNull(result.Error);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void ImportFromCsv_RowsWithEmptyPassword_AreSkipped()
    {
        string csv = "name,url,username,password\nA,https://a.com,u1,\nB,https://b.com,u2,pw2\n";
        string path = WriteTempCsv(csv);

        var result = BrowserImporter.ImportFromCsv(path);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("B", entry.Title);
    }

    [Fact]
    public void ImportFromCsv_FileNotFound_ReturnsError()
    {
        var result = BrowserImporter.ImportFromCsv(Path.Combine(Path.GetTempPath(), "PwdToolTest_does_not_exist.csv"));

        Assert.NotNull(result.Error);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void ImportFromCsv_EmptyFile_ReturnsError()
    {
        string path = WriteTempCsv(string.Empty);

        var result = BrowserImporter.ImportFromCsv(path);

        Assert.NotNull(result.Error);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void ImportFromCsv_TitleFallsBackToHostWhenNameColumnMissing()
    {
        string csv = "url,username,password\nhttps://example.com/login,user,pw\n";
        string path = WriteTempCsv(csv);

        var result = BrowserImporter.ImportFromCsv(path);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("example.com", entry.Title);
    }
}

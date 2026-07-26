using PwdTool.Models;
using Xunit;

namespace PwdTool.Tests;

public class AccountEntryTests
{
    [Fact]
    public void RecencyScore_NeverUsedEntry_RanksBelowAnyUsedEntry()
    {
        // 回归测试：早期版本的排序公式("距今分钟数的无界线性衰减")会让用过 1 次、
        // 但已经有几个小时没打开的账号，分数跌破"从未使用过的新账号"（恒为 0），
        // 导致高频老账号被刚新增却从未使用的账号顶掉。这里验证修复后的公式
        // 恒保证"用过至少一次" > "从未用过"，不论用过之后经过了多久。
        var neverUsed = new AccountEntry { UseCount = 0 };
        var usedOnceLongAgo = new AccountEntry
        {
            UseCount = 1,
            LastUsedUtc = DateTime.UtcNow.AddDays(-30),
        };

        Assert.True(usedOnceLongAgo.RecencyScore > neverUsed.RecencyScore);
    }

    [Fact]
    public void RecencyScore_MoreFrequentAndRecentUsage_RanksHigher()
    {
        var usedOftenRecently = new AccountEntry { UseCount = 50, LastUsedUtc = DateTime.UtcNow.AddMinutes(-5) };
        var usedOnceLongAgo = new AccountEntry { UseCount = 1, LastUsedUtc = DateTime.UtcNow.AddDays(-10) };

        Assert.True(usedOftenRecently.RecencyScore > usedOnceLongAgo.RecencyScore);
    }

    [Theory]
    [InlineData("工作, 银行", new[] { "工作", "银行" })]
    [InlineData("a,a,A", new[] { "a" })] // 大小写不敏感去重
    [InlineData("  spaced  , tags ", new[] { "spaced", "tags" })]
    [InlineData("", new string[0])]
    public void TagsDisplay_Setter_ParsesAndDeduplicates(string input, string[] expected)
    {
        var entry = new AccountEntry { TagsDisplay = input };
        Assert.Equal(expected, entry.Tags);
    }

    [Fact]
    public void TagsDisplay_Setter_NullDoesNotThrow()
    {
        var entry = new AccountEntry();
        entry.TagsDisplay = null!;
        Assert.Empty(entry.Tags);
    }

    [Fact]
    public void TagsDisplay_Getter_JoinsWithCommaSpace()
    {
        var entry = new AccountEntry { Tags = new List<string> { "工作", "银行" } };
        Assert.Equal("工作, 银行", entry.TagsDisplay);
    }

    [Theory]
    [InlineData("标题", "user", "url", "标题")]
    [InlineData("", "user", "url", "user")]
    [InlineData("", "", "https://example.com", "https://example.com")]
    public void DisplayName_FallsBackInOrder(string title, string username, string url, string expected)
    {
        var entry = new AccountEntry { Title = title, Username = username, Url = url };
        Assert.Equal(expected, entry.DisplayName);
    }

    [Fact]
    public void ToString_ReturnsDisplayName()
    {
        var entry = new AccountEntry { Title = "示例" };
        Assert.Equal(entry.DisplayName, entry.ToString());
    }

    [Fact]
    public void Clone_ProducesIndependentCopy()
    {
        var original = new AccountEntry { Title = "原始", Tags = new List<string> { "a" } };
        var clone = original.Clone();

        clone.Title = "修改后";
        clone.Tags.Add("b");

        Assert.Equal("原始", original.Title);
        Assert.Single(original.Tags);
        Assert.Equal(original.Id, clone.Id);
    }
}

using System.Text.Json.Serialization;

namespace PwdTool.Models;

/// <summary>
/// 单条账号密码记录。
/// </summary>
public class AccountEntry
{
    /// <summary>唯一标识。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>标题/备注（例如网站名称）。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>关联网址（可为空，用于浏览器导入去重与展示）。</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>账号/用户名。</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>密码明文（整体文件会通过 DPAPI 加密后落盘）。</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>使用次数，用于"常用"排序。</summary>
    public int UseCount { get; set; }

    /// <summary>最近使用时间，用于"最近"排序。</summary>
    public DateTime LastUsedUtc { get; set; } = DateTime.MinValue;

    /// <summary>创建时间。</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>来源，例如 "手动" / "Chrome" / "Edge"。</summary>
    public string Source { get; set; } = "手动";

    /// <summary>标签/分类（例如"工作"、"游戏"、"银行"），用于搜索框按分类匹配，一条记录可有多个标签。</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>标签的逗号分隔展示形式，供列表/编辑框直接读写。</summary>
    [JsonIgnore]
    public string TagsDisplay
    {
        get => string.Join(", ", Tags);
        set => Tags = value
            .Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>用于展示的名称：优先标题，其次账号，再次网址。</summary>
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title)) return Title;
            if (!string.IsNullOrWhiteSpace(Username)) return Username;
            return Url;
        }
    }

    /// <summary>综合排序权重：近期使用优先，其次使用频次。</summary>
    [JsonIgnore]
    public double RecencyScore
    {
        get
        {
            // 越近使用分值越高，再叠加使用频次的对数权重。
            double recency = LastUsedUtc == DateTime.MinValue
                ? 0
                : -(DateTime.UtcNow - LastUsedUtc).TotalMinutes;
            double frequency = Math.Log(UseCount + 1) * 60; // 频次换算成"分钟"权重
            return recency + frequency;
        }
    }
}

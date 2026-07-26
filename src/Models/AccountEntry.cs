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
        set => Tags = (value ?? string.Empty)
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

    /// <summary>
    /// 综合排序权重：近期使用优先，其次使用频次。
    /// 与早期版本"距今分钟数的无界线性衰减"不同，这里 recency 部分改为指数衰减
    /// （半衰期 72 小时），结果恒在 (0,1] 之间，不会无限变负——旧公式下，用过 1 次的账号
    /// 只要距上次使用超过约 42 分钟，分数就会跌破"从未使用过的新账号"（恒为 0），导致高频
    /// 老账号被刚新增但从未使用的账号顶掉，这是一个真实的排序 bug。现在从未使用过的账号
    /// 统一给一个低于任何"用过的账号"的常数分值（-1），保证只要用过一次就不会被反超。
    /// </summary>
    [JsonIgnore]
    public double RecencyScore
    {
        get
        {
            if (UseCount <= 0)
            {
                return -1;
            }

            const double HalfLifeHours = 72.0;
            double elapsedHours = Math.Max(0, (DateTime.UtcNow - LastUsedUtc).TotalHours);
            double recencyFactor = Math.Pow(0.5, elapsedHours / HalfLifeHours); // (0,1]
            double frequencyScore = Math.Log(UseCount + 1);

            return frequencyScore + recencyFactor;
        }
    }

    /// <summary>供屏幕阅读器等无障碍工具朗读，以及调试/日志展示；ListBox 自绘条目也依赖它作为兜底。</summary>
    public override string ToString() => DisplayName;

    /// <summary>
    /// 浅拷贝一份完全独立的副本（含 Tags 列表本身也是新分配的，避免共享引用）。
    /// 用于编辑对话框：如果直接把 _store.Entries 里的同一个对象实例交给编辑窗口，
    /// 用户在对话框里还没点"确定"就已经原地修改了仍存在于账号库列表中的字段，
    /// "取消"按钮这个语义上应该无副作用的操作实际上并不安全。
    /// </summary>
    public AccountEntry Clone() => new()
    {
        Id = Id,
        Title = Title,
        Url = Url,
        Username = Username,
        Password = Password,
        UseCount = UseCount,
        LastUsedUtc = LastUsedUtc,
        CreatedUtc = CreatedUtc,
        Source = Source,
        Tags = new List<string>(Tags),
    };
}

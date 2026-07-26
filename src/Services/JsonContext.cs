using System.Text.Json.Serialization;
using PwdTool.Models;

namespace PwdTool.Services;

/// <summary>
/// System.Text.Json 源生成序列化上下文：为账号库/设置这两种简单 POCO 提前生成序列化代码，
/// 避免运行时反射。相比纯反射式 JsonSerializer.Serialize/Deserialize&lt;T&gt;()，
/// 这既是性能优化（省去反射开销），也是为将来若要开启发布裁剪(PublishTrimmed)
/// 做准备——反射式序列化在裁剪后的程序集里容易在运行时才暴露"找不到成员"的问题，
/// 源生成上下文没有这个隐患。
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<AccountEntry>))]
[JsonSerializable(typeof(AppSettings))]
internal partial class JsonContext : JsonSerializerContext
{
}

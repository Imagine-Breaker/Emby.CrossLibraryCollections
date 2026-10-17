using MediaBrowser.Model.Plugins;
using System.Collections.Generic;
using Emby.CrossLibraryCollections.Common;

namespace Emby.CrossLibraryCollections
{
    public class SeriesRule
    {
        /// <summary>
        /// 合集目标名称，如 "鬼灭之刃"
        /// </summary>
        public string CollectionName { get; set; } = string.Empty;

        /// <summary>
        /// 匹配别名/关键词（逗号/分号分隔，支持子串模糊匹配），如 "鬼灭之刃,鬼滅の刃,Demon Slayer"
        /// </summary>
        public string Keywords { get; set; } = string.Empty;
    }

    /// <summary>
    /// 候选条目详情（前端展示）
    /// </summary>
    public class CandidateItemInfo
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsNewItem { get; set; } = false;
    }

    /// <summary>
    /// 疑似同系列候选组
    /// </summary>
    public class CandidateGroup
    {
        public string Id { get; set; } = string.Empty;
        public string SuggestedName { get; set; } = string.Empty;
        public bool IsExistingCollection { get; set; } = false;
        public List<CandidateItemInfo> Items { get; set; } = new List<CandidateItemInfo>();
    }

    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// 是否启用定时任务自动扫描
        /// </summary>
        public bool EnableScheduledTask { get; set; } = true;

        /// <summary>
        /// 孤岛保护：只有关联独立作品数 >= 该数值（默认2）时才真正创建合集
        /// </summary>
        public int MinItemsForCollection { get; set; } = 2;

        /// <summary>
        /// 启用反向清理：媒体移走或被删除后，自动将其移出合集
        /// </summary>
        public bool EnableReverseCleanup { get; set; } = true;

        /// <summary>
        /// 智能自动聚类：根据公共标题前缀、脱壳与定界词截断发现同系列
        /// </summary>
        public bool EnableAutoClustering { get; set; } = true;

        /// <summary>
        /// 自定义别名映射规则列表
        /// </summary>
        public List<SeriesRule> CustomRules { get; set; } = new List<SeriesRule>();

        /// <summary>
        /// 题材/标签黑名单（逗号分隔，无修饰空格）
        /// </summary>
        public string BlacklistKeywords { get; set; } = string.Empty;

        /// <summary>
        /// 统一的标题修饰与噪点过滤词列表
        /// </summary>
        public string NoiseAndModifiers { get; set; } = string.Join(",", Constants.DefaultNoiseAndModifiers);

        /// <summary>
        /// 定界分隔词列表
        /// </summary>
        public string DelimiterWords { get; set; } = string.Join(",", Constants.DefaultDelimiterWords);

        /// <summary>
        /// 排除扫描的媒体库 ID/名称列表
        /// </summary>
        public List<string> ExcludedLibraryIds { get; set; } = new List<string>();

        /// <summary>
        /// 自动为没有封面的合集获取并设置海报
        /// </summary>
        public bool AutoSetPoster { get; set; } = true;

        /// <summary>
        /// TMDb API 密钥
        /// </summary>
        public string TmdbApiKey { get; set; } = string.Empty;

        /// <summary>
        /// 用户已采纳/处理过的候选公共前缀列表（避免改名采纳后再次重复推荐原前缀）
        /// </summary>
        public List<string> AdoptedCandidatePrefixes { get; set; } = new List<string>();

        /// <summary>
        /// 用户忽略的候选公共前缀列表（避免反复推荐已忽略的疑似系列）
        /// </summary>
        public List<string> IgnoredCandidatePrefixes { get; set; } = new List<string>();

        /// <summary>
        /// 最近一次扫描生成的疑似同系列候选推荐池
        /// </summary>
        public List<CandidateGroup> PendingCandidates { get; set; } = new List<CandidateGroup>();
    }
}

using MediaBrowser.Model.Plugins;
using System.Collections.Generic;

namespace Emby.CrossLibraryCollections
{
    public class SeriesRule
    {
        /// <summary>
        /// 合集目标名称，如 "鬼灭之刃"
        /// </summary>
        public string CollectionName { get; set; } = string.Empty;

        /// <summary>
        /// 匹配别名/关键词（英文逗号分隔），如 "鬼灭之刃,鬼滅の刃,Demon Slayer"
        /// </summary>
        public string Keywords { get; set; } = string.Empty;
    }

    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// 是否启用定时任务自动扫描
        /// </summary>
        public bool EnableScheduledTask { get; set; } = true;

        /// <summary>
        /// 孤岛保护：只有关联条目 >= 该数值（默认2）时才真正创建合集
        /// </summary>
        public int MinItemsForCollection { get; set; } = 2;

        /// <summary>
        /// 启用反向清理：媒体移走或被删除后，自动将其移出合集
        /// </summary>
        public bool EnableReverseCleanup { get; set; } = true;

        /// <summary>
        /// 智能自动聚类：根据公共标题前缀及路径自动发现同系列（零配置自动合集）
        /// </summary>
        public bool EnableAutoClustering { get; set; } = true;

        /// <summary>
        /// 自定义别名映射规则列表
        /// </summary>
        public List<SeriesRule> CustomRules { get; set; } = new List<SeriesRule>();

        /// <summary>
        /// 题材/标签黑名单（逗号分隔）
        /// </summary>
        public string BlacklistKeywords { get; set; } = string.Empty;

        /// <summary>
        /// 前置修饰词列表（逗号分隔），最高优先级预处理脱壳（如：剧场版, 电影版, OVA, SP）
        /// </summary>
        public string PrefixModifiers { get; set; } = string.Join(", ", Constants.DefaultPrefixModifiers);

        /// <summary>
        /// 定界分隔词列表（逗号分隔），用于截断提取系列名（如：与, 之, ：, :, ·, -, －, ~）
        /// </summary>
        public string DelimiterWords { get; set; } = string.Join(", ", Constants.DefaultDelimiterWords);

        /// <summary>
        /// 附加噪点过滤词（逗号分隔）
        /// </summary>
        public string AdditionalNoiseWords { get; set; } = string.Empty;

        /// <summary>
        /// 排除扫描的媒体库 ID/名称列表
        /// </summary>
        public List<string> ExcludedLibraryIds { get; set; } = new List<string>();

        /// <summary>
        /// 自动为没有封面的合集提取并设置内部影片海报
        /// </summary>
        public bool AutoSetPoster { get; set; } = true;

        /// <summary>
        /// TMDb API 密钥（可选，用于辅助超级宇宙识别；为空时纯本地计算）
        /// </summary>
        public string TmdbApiKey { get; set; } = string.Empty;
    }
}

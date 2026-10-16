using System;
using System.Collections.Generic;

namespace Emby.CrossLibraryCollections.Common
{
    public static class Constants
    {
        /// <summary>
        /// 题材/分类黑名单：绝不允许作为合集名创建
        /// </summary>
        public static readonly HashSet<string> DefaultGenreBlacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "动画", "动漫", "日本动画", "国产动画", "动作", "冒险", "喜剧", "剧情", "科幻", "奇幻",
            "悬疑", "惊悚", "恐怖", "犯罪", "爱情", "战争", "历史", "纪录", "纪录片", "家庭", "音乐",
            "歌舞", "武侠", "古装", "真人秀", "脱口秀", "短片", "西部", "同性", "灾难",
            "Animation", "Action", "Adventure", "Comedy", "Drama", "Sci-Fi", "Science Fiction",
            "Fantasy", "Mystery", "Thriller", "Horror", "Crime", "Romance", "War", "History",
            "Documentary", "Family", "Music", "Western", "Short", "Reality", "Talk",
            "4K", "1080P", "720P", "HDR", "REMUX", "BLURAY", "WEB-DL", "国漫", "美漫", "连载", "完结"
        };

        /// <summary>
        /// 默认前置修饰词（最高优先级预处理脱壳剥离）
        /// </summary>
        public static readonly string[] DefaultPrefixModifiers = new string[]
        {
            "剧场版", "电影版", "动画", "电视剧", "漫改", "特别篇", "OVA", "OAD", "SP"
        };

        /// <summary>
        /// 默认定界分隔词（用于截断提取系列名，如同《哈利·波特与魔法石》截为《哈利·波特》）
        /// </summary>
        public static readonly string[] DefaultDelimiterWords = new string[]
        {
            "与", "之", "：", ":", "·", "-", "－", "~", "篇"
        };

        /// <summary>
        /// 噪点后缀与标记（在比对和提取系列公共名时自动清洗）
        /// </summary>
        public static readonly string[] DefaultNoiseWords = new string[]
        {
            "系列", "合集", "宇宙", "全集", "正片", "特别篇", "外传", "总集篇", "先行版",
            "OVA", "OAD", "SP", "Movie", "The Movie", "Collection", "Franchise", "Universe",
            "Season", "S01", "S02", "S03", "S04", "S05", "S06", "S07", "S08", "S09", "S10",
            "S1", "S2", "S3", "S4", "S5", "S6", "S7", "S8", "S9",
            "第一季", "第二季", "第三季", "第四季", "第五季", "第六季", "第七季", "第八季", "第九季", "第十季",
            "第1季", "第2季", "第3季", "第4季", "第5季", "第6季", "第7季", "第8季", "第9季", "第10季",
            "一期", "二期", "三期", "四期", "part1", "part2", "part 1", "part 2", "part i", "part ii"
        };
    }
}

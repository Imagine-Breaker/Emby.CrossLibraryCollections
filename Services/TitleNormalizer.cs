using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Emby.CrossLibraryCollections.Common;

namespace Emby.CrossLibraryCollections.Services
{
    public static class TitleNormalizer
    {
        // 匹配年份 如 (2020) 或 [2021] 或 【2022】
        private static readonly Regex YearRegex = new Regex(@"[\(\[\{【（]\s*(19\d\d|20\d\d)\s*[\)\]\}】）]", RegexOptions.Compiled);

        // 默认通用标点清除
        private static readonly char[] TrimPunctuation = new[] { ' ', ':', '：', '-', '－', '·', '/', '\\', '_', '~', '，', ',', '。' };

        /// <summary>
        /// 最高优先级预处理：剥离前置修饰词（如“剧场版 ”、“【剧场版】”等脱壳）
        /// </summary>
        public static string StripPrefixModifiers(string rawTitle, IEnumerable<string> prefixModifiers)
        {
            if (string.IsNullOrWhiteSpace(rawTitle))
                return string.Empty;

            var title = rawTitle.Trim();
            var modifiers = (prefixModifiers ?? Constants.DefaultPrefixModifiers)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim())
                .ToList();

            if (modifiers.Count == 0)
                return title;

            bool stripped;
            int maxPasses = 3; // 最多脱壳3层（如【剧场版】【特别篇】）
            do
            {
                stripped = false;
                foreach (var modifier in modifiers)
                {
                    var esc = Regex.Escape(modifier);
                    // 匹配如: ^([【(\[{]?\s*剧场版\s*[】)\]}]?)\s*[:：_\-－\s]*
                    var pattern = $@"^([【(\[{{""]?\s*{esc}\s*[】)\]}}""]?)\s*[:：_\-－\s]*";
                    var match = Regex.Match(title, pattern, RegexOptions.IgnoreCase);
                    if (match.Success && match.Length > 0 && match.Length < title.Length)
                    {
                        title = title.Substring(match.Length).Trim();
                        stripped = true;
                        break;
                    }
                }
                maxPasses--;
            } while (stripped && maxPasses > 0);

            return title;
        }

        /// <summary>
        /// 清洗标题，去除年份、注解噪点与季数词
        /// </summary>
        public static string Normalize(string rawTitle, IEnumerable<string> customNoiseWords = null)
        {
            if (string.IsNullOrWhiteSpace(rawTitle))
                return string.Empty;

            var title = rawTitle.Trim();

            // 1. 去除年份
            title = YearRegex.Replace(title, " ");

            // 2. 去除方括号中的冗余注解 (除单纯中文标题外)
            title = Regex.Replace(title, @"\[.*?\]", " ");
            title = Regex.Replace(title, @"\{.*?\}", " ");

            // 3. 剥离预设噪点与自定义噪点
            var noiseList = new List<string>(Constants.DefaultNoiseWords);
            if (customNoiseWords != null)
            {
                noiseList.AddRange(customNoiseWords);
            }

            // 按长度降序替换，优先匹配长噪点词
            foreach (var noise in noiseList.OrderByDescending(n => n?.Length ?? 0))
            {
                if (string.IsNullOrWhiteSpace(noise)) continue;
                title = Regex.Replace(title, Regex.Escape(noise), " ", RegexOptions.IgnoreCase);
            }

            // 4. 清理多余空格与首尾标点
            title = Regex.Replace(title, @"\s+", " ").Trim(TrimPunctuation);

            return title;
        }

        /// <summary>
        /// 基于定界分隔词截断提取系列名（如《哈利·波特与魔法石》截为《哈利·波特》）
        /// </summary>
        public static string ExtractSeriesByDelimiter(string cleanTitle, IEnumerable<string> delimiterWords)
        {
            if (string.IsNullOrWhiteSpace(cleanTitle))
                return string.Empty;

            var delimiters = (delimiterWords ?? Constants.DefaultDelimiterWords)
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Select(d => d.Trim())
                .ToList();

            if (delimiters.Count == 0)
                return cleanTitle;

            // 寻找最靠前出现的有效定界词
            int earliestIndex = -1;
            string matchedDelimiter = null;

            foreach (var delimiter in delimiters)
            {
                var idx = cleanTitle.IndexOf(delimiter, StringComparison.OrdinalIgnoreCase);
                // 必须在中间位置（idx > 1，确保截断前至少保留2个字符），不能是开头
                if (idx > 1)
                {
                    if (earliestIndex == -1 || idx < earliestIndex)
                    {
                        earliestIndex = idx;
                        matchedDelimiter = delimiter;
                    }
                }
            }

            if (earliestIndex > 1 && matchedDelimiter != null)
            {
                var seriesPart = cleanTitle.Substring(0, earliestIndex).Trim(TrimPunctuation);
                if (seriesPart.Length >= 2)
                {
                    return seriesPart;
                }
            }

            return cleanTitle;
        }
    }
}

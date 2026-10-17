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

        // 标点清理集
        private static readonly char[] TrimPunctuation = new[] { ':', '：', '-', '－', '·', '/', '\\', '_', '~', '，', ',', '。' };

        /// <summary>
        /// 智能脱壳：针对位于开头的修饰词（如“剧场版 鬼灭之刃”、“【新剧场版】头文字D”）进行剥离
        /// </summary>
        public static string StripPrefixModifiers(string rawTitle, IEnumerable<string> modifiers)
        {
            if (string.IsNullOrWhiteSpace(rawTitle))
                return string.Empty;

            var title = rawTitle.Trim();
            var modCandidates = (modifiers ?? Constants.DefaultNoiseAndModifiers)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim())
                .Distinct()
                .OrderByDescending(m => m.Length)
                .ToList();

            if (modCandidates.Count == 0)
                return title;

            bool stripped;
            int maxPasses = 3;
            do
            {
                stripped = false;
                foreach (var mod in modCandidates)
                {
                    var esc = Regex.Escape(mod);
                    var pattern = $@"^([【(\[{{""]?\s*{esc}\s*[】)\]}}""]?)\s*[:：_\-－\s]*";
                    var match = Regex.Match(title, pattern, RegexOptions.IgnoreCase);
                    if (match.Success && match.Length > 0 && match.Length < title.Length)
                    {
                        var remaining = title.Substring(match.Length).Trim();
                        if (remaining.Length >= 2)
                        {
                            title = remaining;
                            stripped = true;
                            break;
                        }
                    }
                }
                maxPasses--;
            } while (stripped && maxPasses > 0);

            return title;
        }

        /// <summary>
        /// 清洗标题，去除年份、注解噪点与季数词（优先按长度降序剥离长词）
        /// </summary>
        public static string Normalize(string rawTitle, IEnumerable<string> noiseAndModifiers = null)
        {
            if (string.IsNullOrWhiteSpace(rawTitle))
                return string.Empty;

            var title = rawTitle.Trim();

            // 1. 去除年份
            title = YearRegex.Replace(title, " ");

            // 2. 去除方括号和花括号注解
            title = Regex.Replace(title, @"\[.*?\]", " ");
            title = Regex.Replace(title, @"\{.*?\}", " ");

            // 3. 剥离噪点与修饰词（长度降序保证“新剧场版”优先于“剧场版”）
            var noiseList = (noiseAndModifiers ?? Constants.DefaultNoiseAndModifiers)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct()
                .OrderByDescending(n => n.Length)
                .ToList();

            foreach (var noise in noiseList)
            {
                title = Regex.Replace(title, Regex.Escape(noise), " ", RegexOptions.IgnoreCase);
            }

            // 4. 清理残存的单个修饰前缀尾巴（如剥离剧场版后残存的“新”字，如“头文字D 新” -> “头文字D”）
            title = Regex.Replace(title, @"[\s:：_\-－·~]+[新旧终]$", " ");

            // 5. 清理首尾标点与两端空格
            title = Regex.Replace(title, @"\s+", " ").Trim().Trim(TrimPunctuation).Trim();

            return title;
        }

        /// <summary>
        /// 基于定界分隔词截断提取系列名（包含安全锁保护，如避免《千与千寻》被截出单字“千”）
        /// </summary>
        public static string ExtractSeriesByDelimiter(string cleanTitle, IEnumerable<string> delimiterWords)
        {
            if (string.IsNullOrWhiteSpace(cleanTitle))
                return cleanTitle;

            var delimiters = (delimiterWords ?? Constants.DefaultDelimiterWords)
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct()
                .ToList();

            if (delimiters.Count == 0)
                return cleanTitle;

            int earliestIndex = -1;
            string matchedDelimiter = null;

            foreach (var delimiter in delimiters)
            {
                var idx = cleanTitle.IndexOf(delimiter, StringComparison.OrdinalIgnoreCase);
                if (idx > 0)
                {
                    if (earliestIndex == -1 || idx < earliestIndex)
                    {
                        earliestIndex = idx;
                        matchedDelimiter = delimiter;
                    }
                }
            }

            if (earliestIndex > 0 && matchedDelimiter != null)
            {
                var candidate = cleanTitle.Substring(0, earliestIndex).Trim().Trim(TrimPunctuation).Trim();

                // 核心安全锁：截断后的系列主名，有效字符数必须 >= 3（如《千与千寻》截出的“千”仅1字被拒绝，而《凉宫春日的忧郁》截出的“凉宫春日”4字被安全保留）
                if (candidate.Length >= 3)
                {
                    return candidate;
                }
            }

            return cleanTitle;
        }

        /// <summary>
        /// 计算两个字符串的最长公共前缀（LCP，忽略标点与大小写）
        /// </summary>
        public static string GetLongestCommonPrefix(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return string.Empty;

            var minLen = Math.Min(s1.Length, s2.Length);
            var i = 0;
            while (i < minLen && char.ToLowerInvariant(s1[i]) == char.ToLowerInvariant(s2[i]))
            {
                i++;
            }

            var prefix = s1.Substring(0, i).Trim().Trim(TrimPunctuation).Trim();
            return prefix;
        }
    }
}

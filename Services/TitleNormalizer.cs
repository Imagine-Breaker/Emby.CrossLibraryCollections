using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Emby.CrossLibraryCollections.Common;

namespace Emby.CrossLibraryCollections.Services
{
    public static class TitleNormalizer
    {
        // 匹配年份 如 (2020) 或 [2021]
        private static readonly Regex YearRegex = new Regex(@"[\(\[\{]\s*(19\d\d|20\d\d)\s*[\)\]\}]", RegexOptions.Compiled);
        
        // 匹配常见的标点分割
        private static readonly char[] Separators = new[] { ':', '：', '-', '－', '·', ' ', '/', '\\' };

        /// <summary>
        /// 清洗标题，去除年份、噪点词、季号及括号噪声
        /// </summary>
        public static string Normalize(string rawTitle, IEnumerable<string> customNoiseWords = null)
        {
            if (string.IsNullOrWhiteSpace(rawTitle))
                return string.Empty;

            var title = rawTitle.Trim();

            // 1. 去除年份
            title = YearRegex.Replace(title, " ");

            // 2. 去除括号中的冗余注解
            title = Regex.Replace(title, @"\[.*?\]", " ");
            title = Regex.Replace(title, @"\{.*?\}", " ");

            // 3. 剥离预设噪点与自定义噪点
            var noiseList = new List<string>(Constants.DefaultNoiseWords);
            if (customNoiseWords != null)
            {
                noiseList.AddRange(customNoiseWords);
            }

            foreach (var noise in noiseList)
            {
                if (string.IsNullOrWhiteSpace(noise)) continue;
                // 不区分大小写替换噪点
                title = Regex.Replace(title, Regex.Escape(noise), " ", RegexOptions.IgnoreCase);
            }

            // 4. 清理首尾与多余空格
            title = Regex.Replace(title, @"\s+", " ").Trim();

            // 5. 如果经过冒号/分号切分，取主前缀
            foreach (var sep in Separators)
            {
                var idx = title.IndexOf(sep);
                if (idx > 1) // 至少保留2个字符前缀
                {
                    var prefixCandidate = title.Substring(0, idx).Trim();
                    if (prefixCandidate.Length >= 2)
                    {
                        title = prefixCandidate;
                        break;
                    }
                }
            }

            return title;
        }

        /// <summary>
        /// 计算两个字符串的最长公共前缀
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

            var prefix = s1.Substring(0, i).Trim(Separators).Trim();
            return prefix;
        }
    }
}

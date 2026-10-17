using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace Emby.CrossLibraryCollections.Services
{
    public class TmdbService
    {
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<string, string> _memoryCache;
        private DateTime _lastRequestTime = DateTime.MinValue;
        private readonly SemaphoreSlim _throttleLock = new SemaphoreSlim(1, 1);

        public TmdbService(ILogger logger, IJsonSerializer jsonSerializer)
        {
            _logger = logger;
            _jsonSerializer = jsonSerializer;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
            _memoryCache = new ConcurrentDictionary<string, string>();
        }

        /// <summary>
        /// 优先从 TMDb 搜索合集（Search Collection）并提取官方高清海报 URL
        /// </summary>
        public async Task<string> SearchCollectionPosterUrlAsync(string collectionName, string apiKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(collectionName) || string.IsNullOrWhiteSpace(apiKey))
                return null;

            var cacheKey = $"poster_{collectionName.Trim().ToLowerInvariant()}";
            if (_memoryCache.TryGetValue(cacheKey, out var cachedPoster))
                return cachedPoster;

            await ThrottleAsync(cancellationToken);

            try
            {
                var queryEscaped = Uri.EscapeDataString(collectionName.Trim());
                var url = $"https://api.themoviedb.org/3/search/collection?query={queryEscaped}&api_key={apiKey}&language=zh-CN";

                var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var reader = new StreamReader(stream))
                    {
                        var content = await reader.ReadToEndAsync().ConfigureAwait(false);
                        // 正则匹配首个结果中的 poster_path: "/xxxx.jpg"
                        var match = Regex.Match(content, @"""poster_path""\s*:\s*""(/[^""]+)""");
                        if (match.Success)
                        {
                            var posterPath = match.Groups[1].Value;
                            var fullPosterUrl = $"https://image.tmdb.org/t/p/w780{posterPath}";
                            _memoryCache[cacheKey] = fullPosterUrl;
                            _logger.Info($"[CrossLibraryCollections] Found TMDb poster for '{collectionName}': {fullPosterUrl}");
                            return fullPosterUrl;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[CrossLibraryCollections] TMDb collection poster search failed for '{collectionName}': {ex.Message}");
            }

            _memoryCache[cacheKey] = null;
            return null;
        }

        /// <summary>
        /// 下载图片字节流
        /// </summary>
        public async Task<byte[]> DownloadImageBytesAsync(string imageUrl, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(imageUrl)) return null;

            try
            {
                return await _httpClient.GetByteArrayAsync(imageUrl).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warn($"[CrossLibraryCollections] Failed to download image from {imageUrl}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取媒体在 TMDb 上的所属合集名称（Movie Collection）
        /// </summary>
        public async Task<string> GetMovieCollectionNameAsync(string tmdbMovieId, string apiKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(tmdbMovieId) || string.IsNullOrWhiteSpace(apiKey))
                return null;

            var cacheKey = $"movie_{tmdbMovieId}";
            if (_memoryCache.TryGetValue(cacheKey, out var cachedName))
                return cachedName;

            await ThrottleAsync(cancellationToken);

            try
            {
                var url = $"https://api.themoviedb.org/3/movie/{tmdbMovieId}?api_key={apiKey}&language=zh-CN";
                var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var reader = new StreamReader(stream))
                    {
                        var content = await reader.ReadToEndAsync().ConfigureAwait(false);
                        if (content.Contains("\"belongs_to_collection\"") && !content.Contains("\"belongs_to_collection\":null"))
                        {
                            var match = Regex.Match(content, @"""belongs_to_collection""\s*:\s*\{[^}]*""name""\s*:\s*""([^""]+)""");
                            if (match.Success)
                            {
                                var collectionName = match.Groups[1].Value;
                                _memoryCache[cacheKey] = collectionName;
                                return collectionName;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[CrossLibraryCollections] TMDb query failed for ID {tmdbMovieId}: {ex.Message}");
            }

            _memoryCache[cacheKey] = null;
            return null;
        }

        private async Task ThrottleAsync(CancellationToken cancellationToken)
        {
            await _throttleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var elapsed = DateTime.UtcNow - _lastRequestTime;
                if (elapsed < TimeSpan.FromMilliseconds(250)) // 严格限制每秒最多 4 次请求
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250) - elapsed, cancellationToken).ConfigureAwait(false);
                }
                _lastRequestTime = DateTime.UtcNow;
            }
            finally
            {
                _throttleLock.Release();
            }
        }
    }
}

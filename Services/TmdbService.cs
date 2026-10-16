using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
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
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
            _memoryCache = new ConcurrentDictionary<string, string>();
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
                        // 简易安全解析 belongs_to_collection
                        if (content.Contains("\"belongs_to_collection\"") && !content.Contains("\"belongs_to_collection\":null"))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(content, @"""belongs_to_collection""\s*:\s*\{[^}]*""name""\s*:\s*""([^""]+)""");
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
                if (elapsed < TimeSpan.FromMilliseconds(250)) // 严格限制每秒最多 4 次请求，保护用户 Key
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

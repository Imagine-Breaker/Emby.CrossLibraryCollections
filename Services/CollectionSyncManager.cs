using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.CrossLibraryCollections.Common;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;

namespace Emby.CrossLibraryCollections.Services
{
    public class CollectionSyncManager
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ICollectionManager _collectionManager;
        private readonly ILogger _logger;
        private readonly TmdbService _tmdbService;

        public CollectionSyncManager(
            ILibraryManager libraryManager,
            ICollectionManager collectionManager,
            ILogger logger,
            TmdbService tmdbService)
        {
            _libraryManager = libraryManager;
            _collectionManager = collectionManager;
            _logger = logger;
            _tmdbService = tmdbService;
        }

        public async Task RunSyncAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var config = Plugin.Instance.Configuration;
            _logger.Info("[CrossLibraryCollections] Starting Cross-Library Collections synchronization...");

            // 1. 构建黑名单字典
            var blacklist = new HashSet<string>(Constants.DefaultGenreBlacklist, StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(config.BlacklistKeywords))
            {
                foreach (var item in config.BlacklistKeywords.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    blacklist.Add(item.Trim());
                }
            }

            // 2. 自定义噪点词列表
            var customNoiseWords = new List<string>();
            if (!string.IsNullOrWhiteSpace(config.AdditionalNoiseWords))
            {
                customNoiseWords.AddRange(config.AdditionalNoiseWords.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
            }

            // 3. 查询服务器所有 Movies 和 Series
            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(Movie).Name, typeof(Series).Name },
                Recursive = true
            };

            var items = _libraryManager.GetItemList(query);
            _logger.Info($"[CrossLibraryCollections] Found {items.Length} total movies & TV series to analyze.");

            // 聚类映射表: 合集名称 -> 对应的 Media Item 列表
            var collectionGroups = new Dictionary<string, List<BaseItem>>(StringComparer.OrdinalIgnoreCase);

            // 4. 第一优先级：用户自定义别名映射规则
            if (config.CustomRules != null && config.CustomRules.Count > 0)
            {
                foreach (var rule in config.CustomRules)
                {
                    if (string.IsNullOrWhiteSpace(rule.CollectionName) || string.IsNullOrWhiteSpace(rule.Keywords))
                        continue;

                    var keywords = rule.Keywords.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(k => k.Trim().ToLowerInvariant())
                        .Where(k => !string.IsNullOrEmpty(k))
                        .ToList();

                    foreach (var item in items)
                    {
                        var itemName = item.Name?.ToLowerInvariant() ?? string.Empty;
                        var originalTitle = item.OriginalTitle?.ToLowerInvariant() ?? string.Empty;
                        var folderName = Path.GetFileName(Path.GetDirectoryName(item.Path) ?? string.Empty)?.ToLowerInvariant() ?? string.Empty;

                        if (keywords.Any(k => itemName.Contains(k) || originalTitle.Contains(k) || folderName.Contains(k)))
                        {
                            AddItemToGroup(collectionGroups, rule.CollectionName.Trim(), item);
                        }
                    }
                }
            }

            // 5. 第二优先级：智能自动聚类 (文本归一化 + 公共前缀 + 物理文件夹回溯)
            if (config.EnableAutoClustering)
            {
                var cleanNameMap = new List<(string CleanName, BaseItem Item)>();
                foreach (var item in items)
                {
                    var clean = TitleNormalizer.Normalize(item.Name, customNoiseWords);
                    if (!string.IsNullOrEmpty(clean) && !blacklist.Contains(clean))
                    {
                        cleanNameMap.Add((clean, item));
                    }
                }

                // 尝试配对跨库项目
                for (int i = 0; i < cleanNameMap.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entryA = cleanNameMap[i];

                    for (int j = i + 1; j < cleanNameMap.Count; j++)
                    {
                        var entryB = cleanNameMap[j];

                        // 如果完全相同，或计算出的公共前缀有效
                        string seriesName = null;
                        if (string.Equals(entryA.CleanName, entryB.CleanName, StringComparison.OrdinalIgnoreCase))
                        {
                            seriesName = entryA.CleanName;
                        }
                        else
                        {
                            var prefix = TitleNormalizer.GetLongestCommonPrefix(entryA.CleanName, entryB.CleanName);
                            if (!string.IsNullOrEmpty(prefix) && prefix.Length >= 2 && !blacklist.Contains(prefix))
                            {
                                seriesName = prefix;
                            }
                        }

                        if (!string.IsNullOrEmpty(seriesName) && !blacklist.Contains(seriesName))
                        {
                            AddItemToGroup(collectionGroups, seriesName, entryA.Item);
                            AddItemToGroup(collectionGroups, seriesName, entryB.Item);
                        }
                    }
                }
            }

            // 6. 获取现有所有 BoxSet（合集）
            var existingBoxSets = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(BoxSet).Name },
                Recursive = true
            }).OfType<BoxSet>().ToList();

            var boxSetMap = existingBoxSets.ToDictionary(b => b.Name, b => b, StringComparer.OrdinalIgnoreCase);

            var totalGroups = collectionGroups.Count;
            var processed = 0;

            // 7. 执行同步到 Emby 合集
            foreach (var kvp in collectionGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processed++;
                progress?.Report((double)processed / Math.Max(1, totalGroups) * 100);

                var collectionName = kvp.Key;
                var targetItems = kvp.Value.Distinct().ToList();

                // 孤岛保护校验
                if (targetItems.Count < config.MinItemsForCollection)
                {
                    continue;
                }

                // 确认合集是否存在，不存在则创建
                BoxSet boxSet;
                if (!boxSetMap.TryGetValue(collectionName, out boxSet))
                {
                    _logger.Info($"[CrossLibraryCollections] Creating new unified collection '{collectionName}' with {targetItems.Count} items.");
                    var createOptions = new CollectionCreationOptions
                    {
                        Name = collectionName,
                        ItemIdList = targetItems.Select(x => x.InternalId).ToArray()
                    };
                    boxSet = await _collectionManager.CreateCollection(createOptions).ConfigureAwait(false);
                    boxSetMap[collectionName] = boxSet;
                }
                else
                {
                    // 增量添加新成员
                    var currentChildrenIds = new HashSet<long>(boxSet.GetChildren(new InternalItemsQuery()).Select(c => c.InternalId));
                    var newItemsToAdd = targetItems.Where(x => !currentChildrenIds.Contains(x.InternalId)).Select(x => x.InternalId).ToArray();

                    if (newItemsToAdd.Length > 0)
                    {
                        _logger.Info($"[CrossLibraryCollections] Adding {newItemsToAdd.Length} items to collection '{collectionName}'.");
                        await _collectionManager.AddToCollection(boxSet.InternalId, newItemsToAdd).ConfigureAwait(false);
                    }

                    // 反向清理：如果启用了反向清理，且原条目不在当前命中范围中，予以移除
                    if (config.EnableReverseCleanup)
                    {
                        var targetIdSet = new HashSet<long>(targetItems.Select(x => x.InternalId));
                        var itemsToRemove = currentChildrenIds.Where(id => !targetIdSet.Contains(id)).ToArray();
                        if (itemsToRemove.Length > 0)
                        {
                            _logger.Info($"[CrossLibraryCollections] Cleaning up {itemsToRemove.Length} stale items from '{collectionName}'.");
                            _collectionManager.RemoveFromCollection(boxSet, itemsToRemove);
                        }
                    }
                }
            }

            _logger.Info("[CrossLibraryCollections] Cross-Library Collections synchronization finished successfully.");
        }

        private static void AddItemToGroup(Dictionary<string, List<BaseItem>> groups, string name, BaseItem item)
        {
            if (string.IsNullOrWhiteSpace(name) || item == null) return;
            if (!groups.TryGetValue(name, out var list))
            {
                list = new List<BaseItem>();
                groups[name] = list;
            }
            if (!list.Any(x => x.InternalId == item.InternalId))
            {
                list.Add(item);
            }
        }
    }
}

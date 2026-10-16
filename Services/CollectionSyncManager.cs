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

        public async Task RunSyncAsync(IProgress<double> progress, CancellationToken cancellationToken, bool cleanAndRebuild = false)
        {
            var config = Plugin.Instance.Configuration;
            _logger.Info($"[CrossLibraryCollections] Starting synchronization (CleanAndRebuild: {cleanAndRebuild})...");

            // 1. 解析黑名单字典
            var blacklist = new HashSet<string>(Constants.DefaultGenreBlacklist, StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(config.BlacklistKeywords))
            {
                foreach (var item in config.BlacklistKeywords.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    blacklist.Add(item.Trim());
                }
            }

            // 2. 解析前置修饰词与定界分隔词列表
            var prefixModifiers = new List<string>();
            if (!string.IsNullOrWhiteSpace(config.PrefixModifiers))
            {
                prefixModifiers.AddRange(config.PrefixModifiers.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
            }
            else
            {
                prefixModifiers.AddRange(Constants.DefaultPrefixModifiers);
            }

            var delimiterWords = new List<string>();
            if (!string.IsNullOrWhiteSpace(config.DelimiterWords))
            {
                delimiterWords.AddRange(config.DelimiterWords.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
            }
            else
            {
                delimiterWords.AddRange(Constants.DefaultDelimiterWords);
            }

            // 3. 解析自定义噪点词列表
            var customNoiseWords = new List<string>();
            if (!string.IsNullOrWhiteSpace(config.AdditionalNoiseWords))
            {
                customNoiseWords.AddRange(config.AdditionalNoiseWords.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
            }

            // 4. 解析排除的媒体库 ID/名称集合
            var excludedLibraries = new HashSet<string>(config.ExcludedLibraryIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            // 5. 查询服务器所有 Movies 和 Series（顶级作品，绝不包含季或集）
            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(Movie).Name, typeof(Series).Name },
                Recursive = true
            };

            var allItems = _libraryManager.GetItemList(query);
            _logger.Info($"[CrossLibraryCollections] Found {allItems.Length} total movies & TV series in Emby.");

            // 过滤掉位于排除媒体库中的条目
            var items = allItems.Where(item => !IsItemInExcludedLibrary(item, excludedLibraries)).ToArray();
            _logger.Info($"[CrossLibraryCollections] After excluding libraries, analyzing {items.Length} items.");

            // 聚类映射表: 合集名称 -> 对应的 BaseItem 列表
            var collectionGroups = new Dictionary<string, List<BaseItem>>(StringComparer.OrdinalIgnoreCase);

            // 6. 第一优先级：用户自定义别名映射规则
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

            // 7. 第二优先级：智能自动聚类
            // 流程：前置修饰词脱壳 -> 标题常规清洗 -> 定界分隔词提取系列主名
            if (config.EnableAutoClustering)
            {
                // 存储: (Item, CleanTitle, SeriesCandidate)
                var processedItems = new List<(BaseItem Item, string CleanTitle, string SeriesCandidate)>();

                foreach (var item in items)
                {
                    // 1) 前置修饰词最高优先级剥离脱壳（如将“剧场版 鬼灭之刃”脱壳为“鬼灭之刃”）
                    var stripped = TitleNormalizer.StripPrefixModifiers(item.Name, prefixModifiers);

                    // 2) 基础标题清洗（去除年份、方括号、噪点词）
                    var clean = TitleNormalizer.Normalize(stripped, customNoiseWords);

                    if (string.IsNullOrWhiteSpace(clean) || blacklist.Contains(clean))
                        continue;

                    // 3) 定界分隔词截断提取（如“哈利·波特与魔法石”截为“哈利·波特”）
                    var seriesCandidate = TitleNormalizer.ExtractSeriesByDelimiter(clean, delimiterWords);

                    processedItems.Add((item, clean, seriesCandidate));
                }

                // 按完全相同的清洗标题聚类
                var byCleanTitle = processedItems.GroupBy(x => x.CleanTitle, StringComparer.OrdinalIgnoreCase);
                foreach (var group in byCleanTitle)
                {
                    if (group.Count() >= 2 && !blacklist.Contains(group.Key))
                    {
                        foreach (var entry in group)
                        {
                            AddItemToGroup(collectionGroups, group.Key, entry.Item);
                        }
                    }
                }

                // 按定界分隔词截断出的系列名聚类
                var bySeriesCandidate = processedItems.GroupBy(x => x.SeriesCandidate, StringComparer.OrdinalIgnoreCase);
                foreach (var group in bySeriesCandidate)
                {
                    // 仅当由定界词产生并且该系列主名下聚集了 >= 2 部作品时
                    if (!string.IsNullOrWhiteSpace(group.Key) && group.Key.Length >= 2 && !blacklist.Contains(group.Key))
                    {
                        // 检查是否提取出了有价值的系列名（不同于原作品本身，或者有多部作品归属于此）
                        if (group.Count() >= config.MinItemsForCollection)
                        {
                            foreach (var entry in group)
                            {
                                AddItemToGroup(collectionGroups, group.Key, entry.Item);
                            }
                        }
                    }
                }
            }

            // 8. 获取现有所有 BoxSet（合集）
            var existingBoxSets = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(BoxSet).Name },
                Recursive = true
            }).OfType<BoxSet>().ToList();

            var boxSetMap = existingBoxSets.ToDictionary(b => b.Name, b => b, StringComparer.OrdinalIgnoreCase);

            // 9. 如果是“清空并重新扫描创建”，清空匹配的旧合集
            if (cleanAndRebuild)
            {
                _logger.Info("[CrossLibraryCollections] Clean & Rebuild requested. Purging existing matched collections...");
                foreach (var kvp in collectionGroups)
                {
                    if (boxSetMap.TryGetValue(kvp.Key, out var oldBoxSet))
                    {
                        try
                        {
                            _logger.Info($"[CrossLibraryCollections] Deleting old collection '{oldBoxSet.Name}' for clean rebuild.");
                            _libraryManager.DeleteItem(oldBoxSet, new DeleteOptions { DeleteFileLocation = true });
                            boxSetMap.Remove(kvp.Key);
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn($"[CrossLibraryCollections] Could not delete collection '{oldBoxSet.Name}', emptying items instead: {ex.Message}");
                            var children = oldBoxSet.GetChildren(new InternalItemsQuery()).Select(c => c.InternalId).ToArray();
                            if (children.Length > 0)
                            {
                                _collectionManager.RemoveFromCollection(oldBoxSet, children);
                            }
                        }
                    }
                }
            }

            var totalGroups = collectionGroups.Count;
            var processed = 0;

            // 10. 执行同步到 Emby 合集
            foreach (var kvp in collectionGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processed++;
                progress?.Report((double)processed / Math.Max(1, totalGroups) * 100);

                var collectionName = kvp.Key;
                var targetItems = kvp.Value.Distinct().ToList();

                // 核心防误判：计算真正的独立作品数量（针对单剧集分多季去重）
                var distinctWorkCount = GetDistinctWorkCount(targetItems);

                // 孤岛保护校验：只有独立作品数 >= MinItemsForCollection 才建合集
                if (distinctWorkCount < config.MinItemsForCollection)
                {
                    _logger.Debug($"[CrossLibraryCollections] Skipped '{collectionName}': distinct works ({distinctWorkCount}) < MinItems ({config.MinItemsForCollection}).");
                    continue;
                }

                // 确认合集是否存在，不存在则创建
                BoxSet boxSet;
                if (!boxSetMap.TryGetValue(collectionName, out boxSet))
                {
                    _logger.Info($"[CrossLibraryCollections] Creating new unified collection '{collectionName}' with {targetItems.Count} items ({distinctWorkCount} distinct works).");
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

                // 11. 自动为没有封面的合集提取并设置内部影片的海报
                if (config.AutoSetPoster && boxSet != null)
                {
                    TryInheritPoster(boxSet, targetItems);
                }
            }

            _logger.Info("[CrossLibraryCollections] Cross-Library Collections synchronization finished successfully.");
        }

        /// <summary>
        /// 检查项目是否属于被排除的媒体库
        /// </summary>
        private static bool IsItemInExcludedLibrary(BaseItem item, HashSet<string> excludedLibraries)
        {
            if (excludedLibraries == null || excludedLibraries.Count == 0 || item == null)
                return false;

            try
            {
                var parents = item.GetParents();
                if (parents != null)
                {
                    foreach (var p in parents)
                    {
                        if (excludedLibraries.Contains(p.Id.ToString()) ||
                            excludedLibraries.Contains(p.InternalId.ToString()) ||
                            excludedLibraries.Contains(p.Name))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }

        /// <summary>
        /// 核心去重：计算独立作品数量，防止单部剧集分了多季被计为多部作品
        /// </summary>
        private static int GetDistinctWorkCount(IEnumerable<BaseItem> items)
        {
            if (items == null) return 0;

            var distinctKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                if (item == null) continue;

                // 优先根据全局媒体标识符 (TMDb / IMDb / TVDb) 进行跨季去重
                var tmdb = item.GetProviderId(MetadataProviders.Tmdb);
                if (!string.IsNullOrWhiteSpace(tmdb))
                {
                    distinctKeys.Add($"tmdb:{tmdb}");
                    continue;
                }

                var imdb = item.GetProviderId(MetadataProviders.Imdb);
                if (!string.IsNullOrWhiteSpace(imdb))
                {
                    distinctKeys.Add($"imdb:{imdb}");
                    continue;
                }

                var tvdb = item.GetProviderId(MetadataProviders.Tvdb);
                if (!string.IsNullOrWhiteSpace(tvdb))
                {
                    distinctKeys.Add($"tvdb:{tvdb}");
                    continue;
                }

                // 如果是 Series，基于 Series 本身的 InternalId 去重
                if (item is Series series)
                {
                    distinctKeys.Add($"series:{series.InternalId}");
                    continue;
                }

                // 默认使用 InternalId
                distinctKeys.Add($"item:{item.InternalId}");
            }

            return distinctKeys.Count;
        }

        /// <summary>
        /// 自动为没有封面的合集提取并设置内部影片的海报
        /// </summary>
        private void TryInheritPoster(BoxSet boxSet, List<BaseItem> targetItems)
        {
            try
            {
                if (boxSet.HasImage(ImageType.Primary))
                    return;

                // 寻找第一个有海报的子项
                var candidate = targetItems.FirstOrDefault(x => x.HasImage(ImageType.Primary));
                if (candidate != null)
                {
                    var imageInfo = candidate.GetImageInfo(ImageType.Primary, 0);
                    if (imageInfo != null && !string.IsNullOrWhiteSpace(imageInfo.Path))
                    {
                        var newImage = new ItemImageInfo
                        {
                            Path = imageInfo.Path,
                            Type = ImageType.Primary
                        };
                        boxSet.SetImage(newImage, 0);
                        boxSet.UpdateToRepository(ItemUpdateType.ImageUpdate);
                        _logger.Info($"[CrossLibraryCollections] Inherited poster for collection '{boxSet.Name}' from '{candidate.Name}'.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[CrossLibraryCollections] Failed to set poster for collection '{boxSet.Name}': {ex.Message}");
            }
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

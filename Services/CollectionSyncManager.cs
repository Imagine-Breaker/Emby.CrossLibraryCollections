using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.CrossLibraryCollections.Common;
using MediaBrowser.Common.Configuration;
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
        private readonly IApplicationPaths _appPaths;

        public CollectionSyncManager(
            ILibraryManager libraryManager,
            ICollectionManager collectionManager,
            ILogger logger,
            TmdbService tmdbService,
            IApplicationPaths appPaths)
        {
            _libraryManager = libraryManager;
            _collectionManager = collectionManager;
            _logger = logger;
            _tmdbService = tmdbService;
            _appPaths = appPaths;
        }

        public async Task RunSyncAsync(IProgress<double> progress, CancellationToken cancellationToken, bool cleanAndRebuild = false)
        {
            var config = Plugin.Instance.Configuration;
            _logger.Info($"[CrossLibraryCollections] Starting synchronization (CleanAndRebuild: {cleanAndRebuild})...");

            // 1. 解析黑名单字典
            var blacklist = new HashSet<string>(Constants.DefaultGenreBlacklist, StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(config.BlacklistKeywords))
            {
                foreach (var item in SplitByDelimiters(config.BlacklistKeywords))
                {
                    blacklist.Add(item);
                }
            }

            // 2. 解析统一的标题修饰与噪点过滤词列表
            var noiseAndModifiers = SplitByDelimiters(config.NoiseAndModifiers);
            if (noiseAndModifiers.Count == 0)
            {
                noiseAndModifiers.AddRange(Constants.DefaultNoiseAndModifiers);
            }

            // 3. 解析定界分隔词列表
            var delimiterWords = SplitDelimitersSetting(config.DelimiterWords);
            if (delimiterWords.Count == 0)
            {
                delimiterWords.AddRange(Constants.DefaultDelimiterWords);
            }

            // 4. 解析排除的媒体库 ID/名称集合
            var excludedLibraries = new HashSet<string>(config.ExcludedLibraryIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            // 5. 查询服务器所有 Movies 和 Series（顶级作品）
            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(Movie).Name, typeof(Series).Name },
                Recursive = true
            };

            var allItems = _libraryManager.GetItemList(query);
            _logger.Info($"[CrossLibraryCollections] Found {allItems.Length} total movies & TV series in Emby.");

            var items = allItems.Where(item => !IsItemInExcludedLibrary(item, excludedLibraries)).ToArray();
            _logger.Info($"[CrossLibraryCollections] After excluding libraries, analyzing {items.Length} items.");

            // 聚类映射表: 合集名称 -> 对应的 BaseItem 列表
            var collectionGroups = new Dictionary<string, List<BaseItem>>(StringComparer.OrdinalIgnoreCase);

            // 候选池映射表: 疑似系列公共前缀 -> 对应的 BaseItem 列表
            var candidateMap = new Dictionary<string, List<BaseItem>>(StringComparer.OrdinalIgnoreCase);
            var ignoredPrefixes = new HashSet<string>(config.IgnoredCandidatePrefixes ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            // 6. 第一优先级：自定义别名映射规则（支持模糊双向匹配）
            if (config.CustomRules != null && config.CustomRules.Count > 0)
            {
                foreach (var rule in config.CustomRules)
                {
                    if (string.IsNullOrWhiteSpace(rule.CollectionName) || string.IsNullOrWhiteSpace(rule.Keywords))
                        continue;

                    var keywords = SplitByDelimiters(rule.Keywords);

                    foreach (var item in items)
                    {
                        var itemName = item.Name ?? string.Empty;
                        var originalTitle = item.OriginalTitle ?? string.Empty;
                        var folderName = Path.GetFileName(Path.GetDirectoryName(item.Path) ?? string.Empty) ?? string.Empty;

                        if (IsFuzzyMatch(keywords, itemName, originalTitle, folderName))
                        {
                            AddItemToGroup(collectionGroups, rule.CollectionName.Trim(), item);
                        }
                    }
                }
            }

            // 7. 第二优先级：智能自动聚类 (高置信度自动归纳 + 疑似同系列候选推导)
            if (config.EnableAutoClustering)
            {
                var processedItems = new List<(BaseItem Item, string CleanTitle, string SeriesCandidate)>();

                foreach (var item in items)
                {
                    // 1) 开头修饰词智能脱壳（如剥离“剧场版 ”、“【新剧场版】”）
                    var stripped = TitleNormalizer.StripPrefixModifiers(item.Name, noiseAndModifiers);

                    // 2) 基础去噪清洗（去除年份、括号注解与常见噪点）
                    var clean = TitleNormalizer.Normalize(stripped, noiseAndModifiers);

                    if (string.IsNullOrWhiteSpace(clean) || blacklist.Contains(clean))
                        continue;

                    // 3) 定界分隔词截断提取系列主名（支持空格截断并带安全锁保护）
                    var seriesCandidate = TitleNormalizer.ExtractSeriesByDelimiter(clean, delimiterWords);

                    processedItems.Add((item, clean, seriesCandidate));
                }

                // 方式 A：清洗后完全同名聚类（高置信度）
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

                // 方式 B：定界分隔词截断匹配（高置信度，如“哈利·波特与...”截出“哈利·波特”）
                var bySeriesCandidate = processedItems.GroupBy(x => x.SeriesCandidate, StringComparer.OrdinalIgnoreCase);
                foreach (var group in bySeriesCandidate)
                {
                    if (!string.IsNullOrWhiteSpace(group.Key) && group.Key.Length >= 2 && !blacklist.Contains(group.Key))
                    {
                        if (group.Count() >= config.MinItemsForCollection)
                        {
                            foreach (var entry in group)
                            {
                                AddItemToGroup(collectionGroups, group.Key, entry.Item);
                            }
                        }
                    }
                }

                // 方式 C：公共前缀比对（推导疑似同系列候选池，如《龙珠GT》《龙珠大魔》《龙珠超》）
                for (int i = 0; i < processedItems.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var itemA = processedItems[i];

                    for (int j = i + 1; j < processedItems.Count; j++)
                    {
                        var itemB = processedItems[j];

                        if (string.Equals(itemA.CleanTitle, itemB.CleanTitle, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var lcp = TitleNormalizer.GetLongestCommonPrefix(itemA.CleanTitle, itemB.CleanTitle);
                        // 公共前缀有效长度必须 >= 2，且不在黑名单及用户忽略名单中
                        if (!string.IsNullOrEmpty(lcp) && lcp.Length >= 2 && !blacklist.Contains(lcp) && !ignoredPrefixes.Contains(lcp))
                        {
                            AddItemToGroup(candidateMap, lcp, itemA.Item);
                            AddItemToGroup(candidateMap, lcp, itemB.Item);
                        }
                    }
                }
            }

            // 8. 合集层级吸收收敛（Sub-Collection Consolidation）
            ConsolidateSubCollections(collectionGroups);

            // 10. 获取现有所有 BoxSet（合集）
            var existingBoxSets = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(BoxSet).Name },
                Recursive = true
            }).OfType<BoxSet>().ToList();

            var boxSetMap = existingBoxSets.ToDictionary(b => b.Name, b => b, StringComparer.OrdinalIgnoreCase);

            // 11. 如果是清空重建，清空旧合集
            if (cleanAndRebuild)
            {
                _logger.Info("[CrossLibraryCollections] Clean & Rebuild requested. Purging matched collections...");
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
                            _logger.Warn($"[CrossLibraryCollections] Could not delete collection '{oldBoxSet.Name}', clearing items instead: {ex.Message}");
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

            // 12. 执行同步到 Emby 合集
            foreach (var kvp in collectionGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processed++;
                progress?.Report((double)processed / Math.Max(1, totalGroups) * 80);

                var collectionName = kvp.Key;
                var targetItems = kvp.Value.Distinct().ToList();

                var distinctWorkCount = GetDistinctWorkCount(targetItems);
                if (distinctWorkCount < config.MinItemsForCollection)
                {
                    _logger.Debug($"[CrossLibraryCollections] Skipped '{collectionName}': distinct works ({distinctWorkCount}) < MinItems ({config.MinItemsForCollection}).");
                    continue;
                }

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
                    var currentChildrenIds = new HashSet<long>(boxSet.GetChildren(new InternalItemsQuery()).Select(c => c.InternalId));
                    var newItemsToAdd = targetItems.Where(x => !currentChildrenIds.Contains(x.InternalId)).Select(x => x.InternalId).ToArray();

                    if (newItemsToAdd.Length > 0)
                    {
                        _logger.Info($"[CrossLibraryCollections] Adding {newItemsToAdd.Length} items to collection '{collectionName}'.");
                        await _collectionManager.AddToCollection(boxSet.InternalId, newItemsToAdd).ConfigureAwait(false);
                    }

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

            // 同步完合集后，重新构建并持久化候选推荐池（排除已被任何已建合集收纳的作品）
            var refreshedBoxSets = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(BoxSet).Name },
                Recursive = true
            }).OfType<BoxSet>().ToList();

            var serverCollectionItemsMap = new Dictionary<string, List<BaseItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in refreshedBoxSets)
            {
                serverCollectionItemsMap[b.Name] = b.GetChildren(new InternalItemsQuery()).ToList();
            }

            var finalCandidateList = BuildPendingCandidates(candidateMap, serverCollectionItemsMap);
            config.PendingCandidates = finalCandidateList;
            Plugin.Instance.SaveConfiguration();
            _logger.Info($"[CrossLibraryCollections] Refreshed {finalCandidateList.Count} pending candidate suggestions for user review.");

            // 13. 强化型封面提取引擎：覆盖全服所有合集
            if (config.AutoSetPoster)
            {
                _logger.Info("[CrossLibraryCollections] Scanning all collections across server for missing poster artwork...");
                var allServerBoxSets = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { typeof(BoxSet).Name },
                    Recursive = true
                }).OfType<BoxSet>().ToList();

                foreach (var set in allServerBoxSets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!HasValidPrimaryImage(set))
                    {
                        await TryEnsureCollectionPosterAsync(set, config.TmdbApiKey, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            progress?.Report(100);
            _logger.Info("[CrossLibraryCollections] Cross-Library Collections synchronization finished successfully.");
        }

        /// <summary>
        /// 快速重新探查疑似候选系列（轻量级，不执行耗时的清空与重建操作）
        /// </summary>
        public Task<List<CandidateGroup>> DiscoverCandidatesOnlyAsync()
        {
            var config = Plugin.Instance.Configuration;
            var blacklist = new HashSet<string>(Constants.DefaultGenreBlacklist, StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(config.BlacklistKeywords))
            {
                foreach (var item in SplitByDelimiters(config.BlacklistKeywords)) blacklist.Add(item);
            }

            var noiseAndModifiers = SplitByDelimiters(config.NoiseAndModifiers);
            if (noiseAndModifiers.Count == 0) noiseAndModifiers.AddRange(Constants.DefaultNoiseAndModifiers);

            var delimiterWords = SplitDelimitersSetting(config.DelimiterWords);
            if (delimiterWords.Count == 0) delimiterWords.AddRange(Constants.DefaultDelimiterWords);

            var excludedLibraries = new HashSet<string>(config.ExcludedLibraryIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var ignoredPrefixes = new HashSet<string>(config.IgnoredCandidatePrefixes ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var adoptedPrefixes = new HashSet<string>(config.AdoptedCandidatePrefixes ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            var allItems = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(Movie).Name, typeof(Series).Name },
                Recursive = true
            });

            var items = allItems.Where(item => !IsItemInExcludedLibrary(item, excludedLibraries)).ToArray();
            var processedItems = new List<(BaseItem Item, string CleanTitle, string SeriesCandidate)>();

            foreach (var item in items)
            {
                var stripped = TitleNormalizer.StripPrefixModifiers(item.Name, noiseAndModifiers);
                var clean = TitleNormalizer.Normalize(stripped, noiseAndModifiers);
                if (!string.IsNullOrWhiteSpace(clean) && !blacklist.Contains(clean))
                {
                    var seriesCandidate = TitleNormalizer.ExtractSeriesByDelimiter(clean, delimiterWords);
                    processedItems.Add((item, clean, seriesCandidate));
                }
            }

            var candidateMap = new Dictionary<string, List<BaseItem>>(StringComparer.OrdinalIgnoreCase);

            // 1) 定界词提取的候选（如《头文字D 第一季》《头文字D 新剧场版》截出《头文字D》）
            var bySeriesCandidate = processedItems.GroupBy(x => x.SeriesCandidate, StringComparer.OrdinalIgnoreCase);
            foreach (var group in bySeriesCandidate)
            {
                if (!string.IsNullOrWhiteSpace(group.Key) && group.Key.Length >= 2 && !blacklist.Contains(group.Key) && !ignoredPrefixes.Contains(group.Key) && !adoptedPrefixes.Contains(group.Key))
                {
                    if (group.Count() >= 2)
                    {
                        foreach (var entry in group)
                        {
                            AddItemToGroup(candidateMap, group.Key, entry.Item);
                        }
                    }
                }
            }

            // 2) 最长公共前缀候选（如《龙珠GT》《龙珠大魔》）
            for (int i = 0; i < processedItems.Count; i++)
            {
                var a = processedItems[i];
                for (int j = i + 1; j < processedItems.Count; j++)
                {
                    var b = processedItems[j];
                    if (string.Equals(a.CleanTitle, b.CleanTitle, StringComparison.OrdinalIgnoreCase)) continue;

                    var lcp = TitleNormalizer.GetLongestCommonPrefix(a.CleanTitle, b.CleanTitle);
                    if (!string.IsNullOrEmpty(lcp) && lcp.Length >= 2 && !blacklist.Contains(lcp) && !ignoredPrefixes.Contains(lcp) && !adoptedPrefixes.Contains(lcp))
                    {
                        AddItemToGroup(candidateMap, lcp, a.Item);
                        AddItemToGroup(candidateMap, lcp, b.Item);
                    }
                }
            }

            var existingBoxSets = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(BoxSet).Name },
                Recursive = true
            }).OfType<BoxSet>().ToDictionary(b => b.Name, b => b, StringComparer.OrdinalIgnoreCase);

            var groups = new Dictionary<string, List<BaseItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in existingBoxSets)
            {
                groups[b.Key] = b.Value.GetChildren(new InternalItemsQuery()).ToList();
            }

            var newDiscovered = BuildPendingCandidates(candidateMap, groups);

            // 防御性合并：绝不因为一次探查就直接抹杀清空已有的未处理候选！
            var existingPending = config.PendingCandidates ?? new List<CandidateGroup>();
            var merged = MergeCandidateLists(existingPending, newDiscovered);

            config.PendingCandidates = merged;
            Plugin.Instance.SaveConfiguration();

            return Task.FromResult(merged);
        }

        private static List<CandidateGroup> MergeCandidateLists(List<CandidateGroup> currentList, List<CandidateGroup> newList)
        {
            var map = new Dictionary<string, CandidateGroup>(StringComparer.OrdinalIgnoreCase);

            if (currentList != null)
            {
                foreach (var g in currentList)
                {
                    if (!string.IsNullOrWhiteSpace(g.SuggestedName))
                    {
                        map[g.SuggestedName] = g;
                    }
                }
            }

            if (newList != null)
            {
                foreach (var g in newList)
                {
                    if (string.IsNullOrWhiteSpace(g.SuggestedName)) continue;
                    if (map.TryGetValue(g.SuggestedName, out var existing))
                    {
                        // 合并条目
                        var existingIds = new HashSet<long>(existing.Items.Select(x => x.Id));
                        foreach (var item in g.Items)
                        {
                            if (!existingIds.Contains(item.Id))
                            {
                                existing.Items.Add(item);
                            }
                        }
                    }
                    else
                    {
                        map[g.SuggestedName] = g;
                    }
                }
            }

            return map.Values.ToList();
        }

        private static string GenerateCandidateId(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix)) return Guid.NewGuid().ToString("N");
            return "cand_" + prefix.Trim().ToLowerInvariant();
        }

        private static List<CandidateGroup> BuildPendingCandidates(Dictionary<string, List<BaseItem>> candidateMap, Dictionary<string, List<BaseItem>> existingGroups)
        {
            var result = new List<CandidateGroup>();

            foreach (var kvp in candidateMap)
            {
                var prefix = kvp.Key;
                var items = kvp.Value.Distinct().ToList();
                bool isExisting = existingGroups.TryGetValue(prefix, out var matchedBoxSetItems);
                var existingIdSet = isExisting ? new HashSet<long>(matchedBoxSetItems.Select(x => x.InternalId)) : new HashSet<long>();

                // 如果存在同名正式合集，把该合集内已有的孩子作品也合并进来呈现给用户微调
                if (isExisting && matchedBoxSetItems != null)
                {
                    foreach (var bi in matchedBoxSetItems)
                    {
                        if (!items.Any(x => x.InternalId == bi.InternalId))
                        {
                            items.Add(bi);
                        }
                    }
                }

                // 只要该群组作品数 >= 2，就推荐出来供用户审核与自由勾选（即使其中某些已被其他合集收纳，也允许用户调整）
                if (items.Count >= 2)
                {
                    result.Add(new CandidateGroup
                    {
                        Id = GenerateCandidateId(prefix),
                        SuggestedName = prefix,
                        IsExistingCollection = isExisting,
                        Items = items.Select(item => new CandidateItemInfo
                        {
                            Id = item.InternalId,
                            Name = item.Name,
                            Type = item.GetType().Name,
                            IsNewItem = isExisting && !existingIdSet.Contains(item.InternalId)
                        }).ToList()
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// 用户一键采纳候选推荐：双向同步（根据 CandidateId 和 SuggestedName 精准剔除原候选，并记录防复发）
        /// </summary>
        public async Task<bool> AdoptCandidateAsync(string candidateId, string suggestedName, string targetCollectionName, long[] itemIds)
        {
            if (string.IsNullOrWhiteSpace(targetCollectionName) || itemIds == null)
                return false;

            var config = Plugin.Instance.Configuration;
            targetCollectionName = targetCollectionName.Trim();
            var targetIdSet = new HashSet<long>(itemIds);

            // 1. 获取现有合集
            var existing = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(BoxSet).Name },
                Recursive = true
            }).OfType<BoxSet>().FirstOrDefault(b => string.Equals(b.Name, targetCollectionName, StringComparison.OrdinalIgnoreCase));

            BoxSet boxSet;
            if (existing == null)
            {
                if (itemIds.Length == 0) return false;

                boxSet = await _collectionManager.CreateCollection(new CollectionCreationOptions
                {
                    Name = targetCollectionName,
                    ItemIdList = itemIds
                }).ConfigureAwait(false);
            }
            else
            {
                boxSet = existing;
                var currentChildrenIds = new HashSet<long>(boxSet.GetChildren(new InternalItemsQuery()).Select(c => c.InternalId));

                // 1) 增量添加新勾选的影片
                var toAdd = itemIds.Where(id => !currentChildrenIds.Contains(id)).ToArray();
                if (toAdd.Length > 0)
                {
                    await _collectionManager.AddToCollection(boxSet.InternalId, toAdd).ConfigureAwait(false);
                }

                // 2) 减量剔除取消勾选的旧片（精准同步）
                var toRemove = currentChildrenIds.Where(id => !targetIdSet.Contains(id)).ToArray();
                if (toRemove.Length > 0)
                {
                    _collectionManager.RemoveFromCollection(boxSet, toRemove);
                }
            }

            // 2. 尝试给合集自动补封面
            if (boxSet != null && config.AutoSetPoster)
            {
                await TryEnsureCollectionPosterAsync(boxSet, config.TmdbApiKey, CancellationToken.None).ConfigureAwait(false);
            }

            // 3. 精准剔除原候选：同时根据 CandidateId、原始 SuggestedName、以及最终 CollectionName 匹配移除！
            if (config.PendingCandidates != null)
            {
                config.PendingCandidates.RemoveAll(c =>
                    (!string.IsNullOrEmpty(candidateId) && string.Equals(c.Id, candidateId, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(suggestedName) && string.Equals(c.SuggestedName, suggestedName, StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(c.SuggestedName, targetCollectionName, StringComparison.OrdinalIgnoreCase));
            }

            // 4. 将原候选前缀记入 AdoptedCandidatePrefixes，防止下一次自动扫描或探查又把原始词捞出来骚扰！
            if (config.AdoptedCandidatePrefixes == null) config.AdoptedCandidatePrefixes = new List<string>();
            if (!string.IsNullOrWhiteSpace(suggestedName) && !config.AdoptedCandidatePrefixes.Contains(suggestedName, StringComparer.OrdinalIgnoreCase))
            {
                config.AdoptedCandidatePrefixes.Add(suggestedName.Trim());
            }

            Plugin.Instance.SaveConfiguration();

            _logger.Info($"[CrossLibraryCollections] Synchronized and adopted candidate id='{candidateId}' suggested='{suggestedName}' target='{targetCollectionName}' with {itemIds.Length} items.");
            return true;
        }

        /// <summary>
        /// 一键采纳所有候选组：批量创建/同步合集并自动沉淀所有规则
        /// </summary>
        public async Task<int> AdoptAllCandidatesAsync(List<Emby.CrossLibraryCollections.Api.BatchAdoptItem> batchItems)
        {
            var config = Plugin.Instance.Configuration;
            int count = 0;

            if (batchItems != null && batchItems.Count > 0)
            {
                foreach (var item in batchItems)
                {
                    if (string.IsNullOrWhiteSpace(item.CollectionName) || item.ItemIds == null || item.ItemIds.Length == 0)
                        continue;

                    var ok = await AdoptCandidateAsync(item.CandidateId, item.SuggestedName, item.CollectionName, item.ItemIds).ConfigureAwait(false);
                    if (ok) count++;
                }
            }
            else if (config.PendingCandidates != null && config.PendingCandidates.Count > 0)
            {
                var copyList = config.PendingCandidates.ToList();
                foreach (var cand in copyList)
                {
                    var itemIds = cand.Items.Select(x => x.Id).ToArray();
                    var ok = await AdoptCandidateAsync(cand.Id, cand.SuggestedName, cand.SuggestedName, itemIds).ConfigureAwait(false);
                    if (ok) count++;
                }
            }

            _logger.Info($"[CrossLibraryCollections] Batch adopted {count} collections successfully.");
            return count;
        }

        /// <summary>
        /// 当新影片入库时触发增量候选分析：若命中现有合集则推荐加入，若无合集但与现有散落影片同系列则推荐新建
        /// </summary>
        public void OnNewItemAdded(BaseItem newItem)
        {
            if (newItem == null) return;
            if (!(newItem is Movie) && !(newItem is Series)) return;

            var config = Plugin.Instance.Configuration;
            var excludedLibraries = new HashSet<string>(config.ExcludedLibraryIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            if (IsItemInExcludedLibrary(newItem, excludedLibraries)) return;

            var noiseAndModifiers = SplitByDelimiters(config.NoiseAndModifiers);
            if (noiseAndModifiers.Count == 0) noiseAndModifiers.AddRange(Constants.DefaultNoiseAndModifiers);

            var delimiterWords = SplitDelimitersSetting(config.DelimiterWords);
            if (delimiterWords.Count == 0) delimiterWords.AddRange(Constants.DefaultDelimiterWords);

            var stripped = TitleNormalizer.StripPrefixModifiers(newItem.Name, noiseAndModifiers);
            var cleanNew = TitleNormalizer.Normalize(stripped, noiseAndModifiers);
            if (string.IsNullOrWhiteSpace(cleanNew)) return;

            var seriesCand = TitleNormalizer.ExtractSeriesByDelimiter(cleanNew, delimiterWords);

            // 1. 查询全服所有已有的 BoxSet
            var existingBoxSets = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(BoxSet).Name },
                Recursive = true
            }).OfType<BoxSet>().ToList();

            var pending = config.PendingCandidates ?? new List<CandidateGroup>();

            // 分支 A：命中现有合集（以候选形式推送待确认）
            BoxSet matchedBoxSet = null;
            foreach (var b in existingBoxSets)
            {
                if (string.Equals(b.Name, seriesCand, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(b.Name, cleanNew, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(seriesCand) && seriesCand.Length >= 2 && b.Name.IndexOf(seriesCand, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    cleanNew.IndexOf(b.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matchedBoxSet = b;
                    break;
                }
            }

            if (matchedBoxSet != null)
            {
                var children = matchedBoxSet.GetChildren(new InternalItemsQuery()).ToList();
                if (children.Any(c => c.InternalId == newItem.InternalId)) return; // 已在合集里

                var candId = GenerateCandidateId(matchedBoxSet.Name);
                var existingCand = pending.FirstOrDefault(c => c.Id == candId || string.Equals(c.SuggestedName, matchedBoxSet.Name, StringComparison.OrdinalIgnoreCase));

                var allItems = new List<CandidateItemInfo>();
                foreach (var c in children)
                {
                    allItems.Add(new CandidateItemInfo { Id = c.InternalId, Name = c.Name, Type = c.GetType().Name, IsNewItem = false });
                }
                allItems.Add(new CandidateItemInfo { Id = newItem.InternalId, Name = newItem.Name, Type = newItem.GetType().Name, IsNewItem = true });

                if (existingCand != null)
                {
                    existingCand.IsExistingCollection = true;
                    existingCand.Items = allItems;
                }
                else
                {
                    pending.Insert(0, new CandidateGroup
                    {
                        Id = candId,
                        SuggestedName = matchedBoxSet.Name,
                        IsExistingCollection = true,
                        Items = allItems
                    });
                }

                config.PendingCandidates = pending;
                Plugin.Instance.SaveConfiguration();
                _logger.Info($"[CrossLibraryCollections] Auto-discovered new item '{newItem.Name}' for existing collection '{matchedBoxSet.Name}'. Added to candidate queue.");
                return;
            }

            // 分支 B：若无匹配合集，与现有影片探查是否有相似前缀群组
            Task.Run(async () =>
            {
                try
                {
                    await DiscoverCandidatesOnlyAsync().ConfigureAwait(false);
                }
                catch { }
            });
        }
        public void IgnoreCandidate(string candidateId, string suggestedName)
        {
            var config = Plugin.Instance.Configuration;

            if (config.IgnoredCandidatePrefixes == null) config.IgnoredCandidatePrefixes = new List<string>();
            if (!string.IsNullOrWhiteSpace(suggestedName) && !config.IgnoredCandidatePrefixes.Contains(suggestedName, StringComparer.OrdinalIgnoreCase))
            {
                config.IgnoredCandidatePrefixes.Add(suggestedName.Trim());
            }

            if (config.PendingCandidates != null)
            {
                config.PendingCandidates.RemoveAll(c =>
                    (!string.IsNullOrEmpty(candidateId) && string.Equals(c.Id, candidateId, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(suggestedName) && string.Equals(c.SuggestedName, suggestedName, StringComparison.OrdinalIgnoreCase)));
            }

            Plugin.Instance.SaveConfiguration();
            _logger.Info($"[CrossLibraryCollections] Ignored candidate id='{candidateId}' prefix='{suggestedName}'.");
        }

        private static void ConsolidateSubCollections(Dictionary<string, List<BaseItem>> groups)
        {
            if (groups == null || groups.Count <= 1) return;

            var keys = groups.Keys.OrderBy(k => k.Length).ToList();
            var toRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < keys.Count; i++)
            {
                var parentKey = keys[i];
                if (toRemove.Contains(parentKey)) continue;

                for (int j = i + 1; j < keys.Count; j++)
                {
                    var childKey = keys[j];
                    if (toRemove.Contains(childKey)) continue;

                    if (childKey.StartsWith(parentKey, StringComparison.OrdinalIgnoreCase))
                    {
                        var suffix = childKey.Substring(parentKey.Length).Trim();
                        if (suffix.Length <= 8 || suffix.Contains("剧场版") || suffix.Contains("篇") || suffix.Contains("Movie"))
                        {
                            foreach (var item in groups[childKey])
                            {
                                AddItemToGroup(groups, parentKey, item);
                            }
                            toRemove.Add(childKey);
                        }
                    }
                }
            }

            foreach (var key in toRemove)
            {
                groups.Remove(key);
            }
        }

        private static bool HasValidPrimaryImage(BaseItem item)
        {
            if (item == null) return false;
            try
            {
                var imageInfo = item.GetImageInfo(ImageType.Primary, 0);
                if (imageInfo != null && !string.IsNullOrWhiteSpace(imageInfo.Path) && File.Exists(imageInfo.Path))
                {
                    return true;
                }

                var path = item.GetImagePath(ImageType.Primary);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// 全服独立刷新所有合集封面（TMDb高清优先，本地剧集海报深度穿透优先）
        /// </summary>
        public async Task<int> RefreshAllCollectionPostersAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var config = Plugin.Instance.Configuration;
            _logger.Info("[CrossLibraryCollections] Starting manual refresh of all collection posters across server...");

            var allServerBoxSets = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(BoxSet).Name },
                Recursive = true
            }).OfType<BoxSet>().ToList();

            int updated = 0;
            int total = allServerBoxSets.Count;
            int idx = 0;

            foreach (var set in allServerBoxSets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                idx++;
                progress?.Report((double)idx / Math.Max(1, total) * 100);

                // 强制更新封面或补全缺失封面
                var ok = await TryEnsureCollectionPosterAsync(set, config.TmdbApiKey, cancellationToken).ConfigureAwait(false);
                if (ok) updated++;
            }

            _logger.Info($"[CrossLibraryCollections] Finished refreshing collection posters. Updated/verified {updated} of {total} boxsets.");
            return updated;
        }

        private async Task<bool> TryEnsureCollectionPosterAsync(BoxSet boxSet, string tmdbApiKey, CancellationToken cancellationToken)
        {
            if (boxSet == null) return false;

            // 1. 优先尝试 TMDb 高清官方合集海报
            if (!string.IsNullOrWhiteSpace(tmdbApiKey))
            {
                try
                {
                    var posterUrl = await _tmdbService.SearchCollectionPosterUrlAsync(boxSet.Name, tmdbApiKey, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(posterUrl))
                    {
                        var imageBytes = await _tmdbService.DownloadImageBytesAsync(posterUrl, cancellationToken).ConfigureAwait(false);
                        if (imageBytes != null && imageBytes.Length > 0)
                        {
                            var saveDir = Path.Combine(_appPaths.CachePath, "CrossLibraryCollections");
                            Directory.CreateDirectory(saveDir);
                            var localFilePath = Path.Combine(saveDir, $"boxset_{boxSet.InternalId}_{Guid.NewGuid():N}.jpg");
                            File.WriteAllBytes(localFilePath, imageBytes);

                            var newImage = new ItemImageInfo
                            {
                                Path = localFilePath,
                                Type = ImageType.Primary
                            };
                            boxSet.SetImage(newImage, 0);
                            boxSet.UpdateToRepository(ItemUpdateType.ImageUpdate);
                            _logger.Info($"[CrossLibraryCollections] Successfully applied TMDb collection poster for '{boxSet.Name}'.");
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn($"[CrossLibraryCollections] Failed to fetch TMDb poster for '{boxSet.Name}': {ex.Message}");
                }
            }

            // 2. 本地封面提取：电视剧绝对优先 (Series First) + 多格式多目录深度穿透，并进行持久化复制
            try
            {
                var children = boxSet.GetChildren(new InternalItemsQuery()).ToList();
                var foundPosterPath = FindChildPosterPath(children);

                if (!string.IsNullOrWhiteSpace(foundPosterPath) && File.Exists(foundPosterPath))
                {
                    // 核心关键：将外部媒体库海报持久化拷贝至插件缓存中，避免虚拟容器外部路径无法入库的问题
                    var saveDir = Path.Combine(_appPaths.CachePath, "CrossLibraryCollections");
                    Directory.CreateDirectory(saveDir);
                    var ext = Path.GetExtension(foundPosterPath);
                    if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
                    var persistedPath = Path.Combine(saveDir, $"boxset_{boxSet.InternalId}_local_{Guid.NewGuid():N}{ext}");
                    File.Copy(foundPosterPath, persistedPath, true);

                    var newImage = new ItemImageInfo
                    {
                        Path = persistedPath,
                        Type = ImageType.Primary
                    };
                    boxSet.SetImage(newImage, 0);
                    boxSet.UpdateToRepository(ItemUpdateType.ImageUpdate);
                    _logger.Info($"[CrossLibraryCollections] Successfully applied and persisted local item poster for '{boxSet.Name}' from '{foundPosterPath}'.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[CrossLibraryCollections] Fallback poster inheritance failed for '{boxSet.Name}': {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// 深度提取子项目海报：严格电视剧优先 (Series > Season > Movie) + 支持全流派本地海报命名
        /// </summary>
        private static string FindChildPosterPath(List<BaseItem> children)
        {
            if (children == null || children.Count == 0) return null;

            // 1. 第一优先级：提取组内的电视剧 (Series) 封面
            var seriesList = children.OfType<Series>().ToList();
            if (seriesList.Count > 0)
            {
                foreach (var s in seriesList)
                {
                    var poster = ResolveItemPosterPath(s);
                    if (!string.IsNullOrWhiteSpace(poster)) return poster;

                    // 穿透提取剧集第一季 (Season 1) 的封面
                    try
                    {
                        var seasons = s.GetChildren(new InternalItemsQuery()).OfType<Season>().OrderBy(sn => sn.IndexNumber ?? 999).ToList();
                        foreach (var sn in seasons)
                        {
                            var seasonPoster = ResolveItemPosterPath(sn);
                            if (!string.IsNullOrWhiteSpace(seasonPoster)) return seasonPoster;
                        }
                    }
                    catch { }
                }
            }

            // 2. 第二优先级：提取组内的电影 (Movie) 封面
            var movieList = children.OfType<Movie>().ToList();
            if (movieList.Count > 0)
            {
                foreach (var m in movieList)
                {
                    var poster = ResolveItemPosterPath(m);
                    if (!string.IsNullOrWhiteSpace(poster)) return poster;
                }
            }

            // 3. 通用兜底：遍历剩余未覆盖的其它条目
            foreach (var item in children)
            {
                var poster = ResolveItemPosterPath(item);
                if (!string.IsNullOrWhiteSpace(poster)) return poster;
            }

            return null;
        }

        /// <summary>
        /// 解析单部作品所在路径及 Emby 缓存中的海报文件
        /// </summary>
        private static string ResolveItemPosterPath(BaseItem item)
        {
            if (item == null) return null;

            // 1) 优先检查 Emby 数据库中已登记的主海报物理文件
            try
            {
                var info = item.GetImageInfo(ImageType.Primary, 0);
                if (info != null && !string.IsNullOrWhiteSpace(info.Path) && File.Exists(info.Path))
                {
                    return info.Path;
                }

                var p = item.GetImagePath(ImageType.Primary);
                if (!string.IsNullOrWhiteSpace(p) && File.Exists(p))
                {
                    return p;
                }
            }
            catch { }

            // 2) 深度扫描磁盘所在目录中的各种可能的海报命名格式
            try
            {
                var itemPath = item.Path;
                if (string.IsNullOrWhiteSpace(itemPath)) return null;

                // 探测可能存在的目录列表（本体目录、上一级文件夹）
                var candidateDirs = new List<string>();
                if (Directory.Exists(itemPath))
                {
                    candidateDirs.Add(itemPath);
                }
                else if (File.Exists(itemPath))
                {
                    var parent = Path.GetDirectoryName(itemPath);
                    if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                    {
                        candidateDirs.Add(parent);
                    }
                }

                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(itemPath);

                var commonNames = new List<string>
                {
                    "poster.jpg", "poster.png", "poster.webp", "poster.jpeg",
                    "folder.jpg", "folder.png", "folder.webp", "folder.jpeg",
                    "cover.jpg", "cover.png", "cover.webp", "cover.jpeg"
                };

                // 支持电影名/剧名同名海报，如 MovieName-poster.jpg, MovieName.jpg
                if (!string.IsNullOrWhiteSpace(fileNameWithoutExt))
                {
                    commonNames.Add($"{fileNameWithoutExt}-poster.jpg");
                    commonNames.Add($"{fileNameWithoutExt}-poster.png");
                    commonNames.Add($"{fileNameWithoutExt}-poster.webp");
                    commonNames.Add($"{fileNameWithoutExt}.jpg");
                    commonNames.Add($"{fileNameWithoutExt}.png");
                }

                foreach (var dir in candidateDirs)
                {
                    foreach (var name in commonNames)
                    {
                        var candidateFile = Path.Combine(dir, name);
                        if (File.Exists(candidateFile))
                        {
                            return candidateFile;
                        }
                    }

                    // 深度通配符扫描目录下的 *poster* 文件
                    try
                    {
                        var posterFiles = Directory.GetFiles(dir, "*poster*.*", SearchOption.TopDirectoryOnly);
                        foreach (var f in posterFiles)
                        {
                            var ext = Path.GetExtension(f).ToLowerInvariant();
                            if (ext == ".jpg" || ext == ".png" || ext == ".webp" || ext == ".jpeg")
                            {
                                return f;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        private static bool IsFuzzyMatch(List<string> keywords, string itemName, string originalTitle, string folderName)
        {
            var cleanItem = TitleNormalizer.Normalize(itemName).ToLowerInvariant();
            var cleanOrig = TitleNormalizer.Normalize(originalTitle).ToLowerInvariant();
            var cleanFolder = TitleNormalizer.Normalize(folderName).ToLowerInvariant();

            foreach (var kw in keywords)
            {
                if (string.IsNullOrWhiteSpace(kw)) continue;
                var cleanKw = TitleNormalizer.Normalize(kw).ToLowerInvariant();
                if (string.IsNullOrEmpty(cleanKw)) cleanKw = kw.Trim().ToLowerInvariant();

                if (cleanItem.Contains(cleanKw) || cleanOrig.Contains(cleanKw) || cleanFolder.Contains(cleanKw))
                    return true;

                if (cleanKw.Length >= 3 && cleanKw.Contains(cleanItem) && cleanItem.Length >= 3)
                    return true;
            }

            return false;
        }

        private static List<string> SplitByDelimiters(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            return text.Split(new[] { ',', '，', ';', '；', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
                       .Where(s => !string.IsNullOrEmpty(s))
                       .ToList();
        }

        private static List<string> SplitDelimitersSetting(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return list;

            var rawTokens = text.Split(new[] { ',', '，', ';', '；', '\r', '\n' }, StringSplitOptions.None);
            foreach (var token in rawTokens)
            {
                if (token == " " || token.Equals("[空格]", StringComparison.OrdinalIgnoreCase) || token.Equals("<space>", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(" ");
                    continue;
                }

                var trimmed = token.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    list.Add(trimmed);
                }
            }

            return list;
        }

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
            catch { }

            return false;
        }

        private static int GetDistinctWorkCount(IEnumerable<BaseItem> items)
        {
            if (items == null) return 0;
            var distinctKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                if (item == null) continue;

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

                if (item is Series series)
                {
                    distinctKeys.Add($"series:{series.InternalId}");
                    continue;
                }

                distinctKeys.Add($"item:{item.InternalId}");
            }

            return distinctKeys.Count;
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

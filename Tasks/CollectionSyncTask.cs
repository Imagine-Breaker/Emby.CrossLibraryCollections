using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emby.CrossLibraryCollections.Services;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Emby.CrossLibraryCollections.Tasks
{
    public class CollectionSyncTask : IScheduledTask
    {
        private readonly CollectionSyncManager _syncManager;
        private readonly ILogger _logger;

        public CollectionSyncTask(CollectionSyncManager syncManager, ILogger logger)
        {
            _syncManager = syncManager;
            _logger = logger;
        }

        public string Name => "跨库系列合集同步 (Cross-Library Collection Sync)";

        public string Key => "CrossLibraryCollectionSyncTask";

        public string Description => "自动扫描电影库和电视剧库，清洗标题噪点并根据规则将同系列影片聚合成统一的跨库合集。";

        public string Category => "Library";

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            if (!Plugin.Instance.Configuration.EnableScheduledTask)
            {
                _logger.Info("[CrossLibraryCollections] Scheduled task skipped as it is disabled in settings.");
                return;
            }

            await _syncManager.RunSyncAsync(progress, cancellationToken).ConfigureAwait(false);
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // 默认每天凌晨 3:00 自动执行一次
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerDaily,
                    TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
                }
            };
        }
    }
}

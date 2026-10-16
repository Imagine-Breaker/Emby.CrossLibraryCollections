using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Services;

namespace Emby.CrossLibraryCollections.Api
{
    [Route("/CrossLibraryCollections/Rebuild", "POST", Summary = "清空并重新扫描创建跨库合集")]
    public class RebuildCollectionsRequest : IReturnVoid
    {
    }

    public class RebuildService : IService
    {
        public void Post(RebuildCollectionsRequest request)
        {
            Task.Run(async () =>
            {
                try
                {
                    await Plugin.Instance.SyncManager.RunSyncAsync(null, CancellationToken.None, cleanAndRebuild: true).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // 记录于日志
                }
            });
        }
    }
}

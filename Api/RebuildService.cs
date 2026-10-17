using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using MediaBrowser.Model.Services;

namespace Emby.CrossLibraryCollections.Api
{
    [Route("/CrossLibraryCollections/Rebuild", "POST", Summary = "清空并重新扫描创建跨库合集")]
    public class RebuildCollectionsRequest : IReturnVoid
    {
    }

    [Route("/CrossLibraryCollections/Candidates", "GET", Summary = "获取疑似同系列候选推荐列表")]
    public class GetCandidatesRequest : IReturn<List<CandidateGroup>>
    {
    }

    [Route("/CrossLibraryCollections/DiscoverCandidates", "POST", Summary = "快速重新探查疑似同系列候选")]
    public class DiscoverCandidatesRequest : IReturn<List<CandidateGroup>>
    {
    }

    [Route("/CrossLibraryCollections/AdoptCandidate", "POST", Summary = "一键采纳疑似候选组并创建合集")]
    public class AdoptCandidateRequest : IReturn<bool>
    {
        public string CandidateId { get; set; }
        public string SuggestedName { get; set; }
        public string CollectionName { get; set; }
        public long[] ItemIds { get; set; }
    }

    public class BatchAdoptItem
    {
        public string CandidateId { get; set; }
        public string SuggestedName { get; set; }
        public string CollectionName { get; set; }
        public long[] ItemIds { get; set; }
    }

    [Route("/CrossLibraryCollections/AdoptAllCandidates", "POST", Summary = "一键采纳所有候选组")]
    public class AdoptAllCandidatesRequest : IReturn<int>
    {
        public List<BatchAdoptItem> Candidates { get; set; }
    }

    [Route("/CrossLibraryCollections/IgnoreCandidate", "POST", Summary = "忽略疑似候选组")]
    public class IgnoreCandidateRequest : IReturnVoid
    {
        public string CandidateId { get; set; }
        public string SuggestedName { get; set; }
    }

    [Route("/CrossLibraryCollections/RefreshPosters", "POST", Summary = "立即重新检查并补全所有合集封面")]
    public class RefreshPostersRequest : IReturnVoid
    {
    }

    [Route("/CrossLibraryCollections/Version", "GET", Summary = "获取插件运行时的真实程序集版本")]
    public class GetVersionRequest : IReturn<string>
    {
    }

    public class CandidateService : IService
    {
        public void Post(RebuildCollectionsRequest request)
        {
            Task.Run(async () =>
            {
                try
                {
                    await Plugin.Instance.SyncManager.RunSyncAsync(null, System.Threading.CancellationToken.None, cleanAndRebuild: true).ConfigureAwait(false);
                }
                catch { }
            });
        }

        public object Get(GetCandidatesRequest request)
        {
            return Plugin.Instance.Configuration.PendingCandidates ?? new List<CandidateGroup>();
        }

        public async Task<object> Post(DiscoverCandidatesRequest request)
        {
            try
            {
                var candidates = await Plugin.Instance.SyncManager.DiscoverCandidatesOnlyAsync().ConfigureAwait(false);
                return candidates ?? new List<CandidateGroup>();
            }
            catch (Exception ex)
            {
                Plugin.Instance.Logger.ErrorException("[CrossLibraryCollections] Error in DiscoverCandidates API", ex);
                return Plugin.Instance.Configuration.PendingCandidates ?? new List<CandidateGroup>();
            }
        }

        public async Task<object> Post(AdoptCandidateRequest request)
        {
            try
            {
                var ok = await Plugin.Instance.SyncManager.AdoptCandidateAsync(request.CandidateId, request.SuggestedName, request.CollectionName, request.ItemIds).ConfigureAwait(false);
                return ok;
            }
            catch (Exception ex)
            {
                Plugin.Instance.Logger.ErrorException("[CrossLibraryCollections] Error in AdoptCandidate API", ex);
                return false;
            }
        }

        public async Task<object> Post(AdoptAllCandidatesRequest request)
        {
            try
            {
                var count = await Plugin.Instance.SyncManager.AdoptAllCandidatesAsync(request.Candidates).ConfigureAwait(false);
                return count;
            }
            catch (Exception ex)
            {
                Plugin.Instance.Logger.ErrorException("[CrossLibraryCollections] Error in AdoptAllCandidates API", ex);
                return 0;
            }
        }

        public void Post(RefreshPostersRequest request)
        {
            Task.Run(async () =>
            {
                try
                {
                    await Plugin.Instance.SyncManager.RefreshAllCollectionPostersAsync(null, System.Threading.CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Plugin.Instance.Logger.ErrorException("[CrossLibraryCollections] Error in RefreshPosters task", ex);
                }
            });
        }

        public void Post(IgnoreCandidateRequest request)
        {
            Plugin.Instance.SyncManager.IgnoreCandidate(request.CandidateId, request.SuggestedName);
        }

        public object Get(GetVersionRequest request)
        {
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            return ver != null ? ver.ToString() : "1.3.5.0";
        }
    }
}

using System;
using System.Collections.Generic;
using Emby.CrossLibraryCollections.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Emby.CrossLibraryCollections
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public static Plugin Instance { get; private set; }

        public override string Name => "Cross-Library Collections (跨库同系列合集)";

        public override string Description => "自动将同系列的电影和电视剧聚合成统一的Emby合集。支持全服封面TMDb智能获取、单剧多季防误判与定界词截断。";

        public override Guid Id => Guid.Parse("9B52DE87-D306-444F-A16F-E64B0175BD4E");

        public CollectionSyncManager SyncManager { get; private set; }
        public TmdbService TmdbService { get; private set; }
        public ILogger Logger { get; private set; }

        public Plugin(
            IApplicationPaths appPaths,
            IXmlSerializer xmlSerializer,
            ILibraryManager libraryManager,
            ICollectionManager collectionManager,
            ILogManager logManager,
            IJsonSerializer jsonSerializer)
            : base(appPaths, xmlSerializer)
        {
            Instance = this;
            Logger = logManager.GetLogger(Name);
            TmdbService = new TmdbService(Logger, jsonSerializer);
            SyncManager = new CollectionSyncManager(libraryManager, collectionManager, Logger, TmdbService, appPaths);

            // 监听新影片入库事件，自动进行增量候选发现
            try
            {
                libraryManager.ItemAdded += (sender, args) =>
                {
                    if (args?.Item != null)
                    {
                        SyncManager.OnNewItemAdded(args.Item);
                    }
                };
            }
            catch (Exception ex)
            {
                Logger.ErrorException("[CrossLibraryCollections] Failed to hook ItemAdded event", ex);
            }
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "CrossLibraryCollectionsConfig",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html",
                    EnableInMainMenu = true,
                    MenuSection = "server",
                    MenuIcon = "auto_awesome_motion",
                    DisplayName = "跨库同系列合集"
                },
                new PluginPageInfo
                {
                    Name = "CrossLibraryCollectionsConfigJs",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.js"
                }
            };
        }
    }
}

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

        public override string Description => "自动将同系列的电影和电视剧（如动画+剧场版、漫威MCU等）聚合成统一的Emby合集。支持无标签零配置运行，内置防误创与去噪算法。";

        public override Guid Id => Guid.Parse("9B52DE87-D306-444F-A16F-E64B0175BD4E");

        public CollectionSyncManager SyncManager { get; private set; }
        public TmdbService TmdbService { get; private set; }

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
            var logger = logManager.GetLogger(Name);
            TmdbService = new TmdbService(logger, jsonSerializer);
            SyncManager = new CollectionSyncManager(libraryManager, collectionManager, logger, TmdbService);
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "CrossLibraryCollectionsConfig",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html",
                    EnableInMainMenu = false
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

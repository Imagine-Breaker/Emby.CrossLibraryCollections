define(['emby-button', 'emby-checkbox', 'emby-input'], function () {
    return function (page, params) {

        var pluginId = "9B52DE87-D306-444F-A16F-E64B0175BD4E";

        function renderRules(rules) {
            var container = page.querySelector('#rulesListContainer');
            container.innerHTML = '';

            (rules || []).forEach(function (rule) {
                var div = document.createElement('div');
                div.style.display = 'flex';
                div.style.gap = '10px';
                div.style.marginBottom = '10px';
                div.style.alignItems = 'center';

                div.innerHTML =
                    '<div style="flex:1;">' +
                    '<input is="emby-input" type="text" class="rule-name" placeholder="合集名称 (如: 鬼灭之刃)" value="' + (rule.CollectionName || '') + '" />' +
                    '</div>' +
                    '<div style="flex:2;">' +
                    '<input is="emby-input" type="text" class="rule-keywords" placeholder="包含关键词 (英文逗号分隔，如: 鬼灭之刃,鬼滅の刃,无限列车)" value="' + (rule.Keywords || '') + '" />' +
                    '</div>' +
                    '<button is="emby-button" type="button" class="btn-remove-rule raised" style="background:#cc3333;color:#fff;">删除</button>';

                div.querySelector('.btn-remove-rule').addEventListener('click', function () {
                    div.remove();
                });

                container.appendChild(div);
            });
        }

        function loadVirtualFolders(excludedIds) {
            var container = page.querySelector('#excludedLibrariesContainer');
            var excludedSet = {};
            (excludedIds || []).forEach(function (id) {
                excludedSet[id] = true;
            });

            var getFolders = ApiClient.getVirtualFolders ? ApiClient.getVirtualFolders() : ApiClient.getJSON(ApiClient.getUrl('Library/VirtualFolders'));
            getFolders.then(function (result) {
                var folders = result.Items || result || [];
                container.innerHTML = '';

                if (folders.length === 0) {
                    container.innerHTML = '<span style="color:#888;">未发现可供排除的媒体库</span>';
                    return;
                }

                folders.forEach(function (folder) {
                    var id = folder.ItemId || folder.Id || folder.Name;
                    var name = folder.Name || '未命名媒体库';
                    var isChecked = !!(excludedSet[id] || excludedSet[folder.Name] || excludedSet[folder.ItemId]);

                    var label = document.createElement('label');
                    label.className = 'checkboxContainer';
                    label.style.display = 'inline-flex';
                    label.style.alignItems = 'center';
                    label.style.margin = '4px 0';

                    label.innerHTML =
                        '<input is="emby-checkbox" type="checkbox" class="chk-library" data-id="' + id + '" data-name="' + name + '"' + (isChecked ? ' checked' : '') + ' />' +
                        '<span>' + name + (folder.CollectionType ? ' (' + folder.CollectionType + ')' : '') + '</span>';

                    container.appendChild(label);
                });
            }).catch(function () {
                container.innerHTML = '<span style="color:#cc3333;">获取媒体库列表失败，请刷新重试</span>';
            });
        }

        page.addEventListener('viewshow', function () {
            Dashboard.showLoadingMsg();

            ApiClient.getPluginConfiguration(pluginId).then(function (config) {
                page.querySelector('#chkEnableTask').checked = config.EnableScheduledTask;
                page.querySelector('#chkAutoClustering').checked = config.EnableAutoClustering;
                page.querySelector('#chkReverseCleanup').checked = config.EnableReverseCleanup;
                page.querySelector('#chkAutoSetPoster').checked = config.AutoSetPoster !== false;
                page.querySelector('#txtMinItems').value = config.MinItemsForCollection || 2;
                page.querySelector('#txtPrefixModifiers').value = config.PrefixModifiers || '';
                page.querySelector('#txtDelimiterWords').value = config.DelimiterWords || '';
                page.querySelector('#txtBlacklist').value = config.BlacklistKeywords || '';
                page.querySelector('#txtNoiseWords').value = config.AdditionalNoiseWords || '';
                page.querySelector('#txtTmdbKey').value = config.TmdbApiKey || '';

                renderRules(config.CustomRules || []);
                loadVirtualFolders(config.ExcludedLibraryIds || []);

                Dashboard.hideLoadingMsg();
            });
        });

        page.querySelector('#btnAddRule').addEventListener('click', function () {
            var container = page.querySelector('#rulesListContainer');
            var div = document.createElement('div');
            div.style.display = 'flex';
            div.style.gap = '10px';
            div.style.marginBottom = '10px';
            div.style.alignItems = 'center';

            div.innerHTML =
                '<div style="flex:1;">' +
                '<input is="emby-input" type="text" class="rule-name" placeholder="合集名称 (如: 漫威电影宇宙)" />' +
                '</div>' +
                '<div style="flex:2;">' +
                '<input is="emby-input" type="text" class="rule-keywords" placeholder="包含关键词 (如: 复仇者联盟,钢铁侠,洛基,旺达幻视)" />' +
                '</div>' +
                '<button is="emby-button" type="button" class="btn-remove-rule raised" style="background:#cc3333;color:#fff;">删除</button>';

            div.querySelector('.btn-remove-rule').addEventListener('click', function () {
                div.remove();
            });

            container.appendChild(div);
        });

        // 保存配置
        page.querySelector('#collectionsConfigForm').addEventListener('submit', function (e) {
            e.preventDefault();
            Dashboard.showLoadingMsg();

            ApiClient.getPluginConfiguration(pluginId).then(function (config) {
                config.EnableScheduledTask = page.querySelector('#chkEnableTask').checked;
                config.EnableAutoClustering = page.querySelector('#chkAutoClustering').checked;
                config.EnableReverseCleanup = page.querySelector('#chkReverseCleanup').checked;
                config.AutoSetPoster = page.querySelector('#chkAutoSetPoster').checked;
                config.MinItemsForCollection = parseInt(page.querySelector('#txtMinItems').value, 10) || 2;
                config.PrefixModifiers = page.querySelector('#txtPrefixModifiers').value;
                config.DelimiterWords = page.querySelector('#txtDelimiterWords').value;
                config.BlacklistKeywords = page.querySelector('#txtBlacklist').value;
                config.AdditionalNoiseWords = page.querySelector('#txtNoiseWords').value;
                config.TmdbApiKey = page.querySelector('#txtTmdbKey').value;

                // 收集排除的媒体库
                var excluded = [];
                var libCheckboxes = page.querySelectorAll('.chk-library');
                libCheckboxes.forEach(function (chk) {
                    if (chk.checked) {
                        var id = chk.getAttribute('data-id');
                        var name = chk.getAttribute('data-name');
                        if (id) excluded.push(id);
                        if (name && name !== id) excluded.push(name);
                    }
                });
                config.ExcludedLibraryIds = excluded;

                // 收集自定义规则
                var rules = [];
                var ruleElements = page.querySelectorAll('#rulesListContainer > div');
                ruleElements.forEach(function (el) {
                    var name = el.querySelector('.rule-name').value.trim();
                    var keywords = el.querySelector('.rule-keywords').value.trim();
                    if (name && keywords) {
                        rules.push({
                            CollectionName: name,
                            Keywords: keywords
                        });
                    }
                });
                config.CustomRules = rules;

                ApiClient.updatePluginConfiguration(pluginId, config).then(function (result) {
                    Dashboard.processPluginConfigurationUpdateResult(result);
                });
            });

            return false;
        });

        // 清空并全量重新扫描创建
        page.querySelector('#btnRebuild').addEventListener('click', function () {
            var confirmed = confirm("⚠️ 警告：确定要清空由本插件生成的合集并进行全量重新扫描创建吗？\n\n该操作会立即在后台全量执行，日常的定时任务不会清空合集。");
            if (!confirmed) return;

            Dashboard.showLoadingMsg();
            var rebuildUrl = ApiClient.getUrl('CrossLibraryCollections/Rebuild');

            var req = ApiClient.ajax ? ApiClient.ajax({ type: 'POST', url: rebuildUrl }) : fetch(rebuildUrl, { method: 'POST' });
            Promise.resolve(req).then(function () {
                Dashboard.hideLoadingMsg();
                Dashboard.alert("🚀 全量清空与重建任务已在服务端后台启动！\n\n您可以前往 Emby『合集』界面或查看 Emby 日志关注同步进度。");
            }).catch(function (err) {
                Dashboard.hideLoadingMsg();
                Dashboard.alert("启动重建任务失败: " + (err.message || err));
            });
        });
    };
});

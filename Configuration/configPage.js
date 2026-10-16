define(['emby-button', 'emby-checkbox', 'emby-input'], function () {
    return function (page, params) {

        var pluginId = "9B52DE87-D306-444F-A16F-E64B0175BD4E";

        function renderRules(rules) {
            var container = page.querySelector('#rulesListContainer');
            container.innerHTML = '';

            (rules || []).forEach(function (rule, index) {
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

        page.addEventListener('viewshow', function () {
            Dashboard.showLoadingMsg();

            ApiClient.getPluginConfiguration(pluginId).then(function (config) {
                page.querySelector('#chkEnableTask').checked = config.EnableScheduledTask;
                page.querySelector('#chkAutoClustering').checked = config.EnableAutoClustering;
                page.querySelector('#chkReverseCleanup').checked = config.EnableReverseCleanup;
                page.querySelector('#txtMinItems').value = config.MinItemsForCollection || 2;
                page.querySelector('#txtBlacklist').value = config.BlacklistKeywords || '';
                page.querySelector('#txtNoiseWords').value = config.AdditionalNoiseWords || '';
                page.querySelector('#txtTmdbKey').value = config.TmdbApiKey || '';

                renderRules(config.CustomRules || []);

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

        page.querySelector('#collectionsConfigForm').addEventListener('submit', function (e) {
            e.preventDefault();
            Dashboard.showLoadingMsg();

            ApiClient.getPluginConfiguration(pluginId).then(function (config) {
                config.EnableScheduledTask = page.querySelector('#chkEnableTask').checked;
                config.EnableAutoClustering = page.querySelector('#chkAutoClustering').checked;
                config.EnableReverseCleanup = page.querySelector('#chkReverseCleanup').checked;
                config.MinItemsForCollection = parseInt(page.querySelector('#txtMinItems').value, 10) || 2;
                config.BlacklistKeywords = page.querySelector('#txtBlacklist').value;
                config.AdditionalNoiseWords = page.querySelector('#txtNoiseWords').value;
                config.TmdbApiKey = page.querySelector('#txtTmdbKey').value;

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
    };
});

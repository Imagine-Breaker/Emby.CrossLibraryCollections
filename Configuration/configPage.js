define(['emby-button', 'emby-checkbox', 'emby-input'], function () {
    return function (page, params) {

        var pluginId = "9B52DE87-D306-444F-A16F-E64B0175BD4E";

        function safeParseArray(data) {
            if (!data) return [];
            if (Array.isArray(data)) return data;
            if (typeof data === 'string') {
                try {
                    var parsed = JSON.parse(data);
                    if (Array.isArray(parsed)) return parsed;
                    if (parsed && Array.isArray(parsed.Items)) return parsed.Items;
                } catch (e) {
                    return [];
                }
            }
            if (typeof data === 'object') {
                if (Array.isArray(data.Items)) return data.Items;
                if (Array.isArray(data.items)) return data.items;
            }
            return [];
        }

        function renderCandidates(candidates) {
            var container = page.querySelector('#candidatesListContainer');
            var badge = page.querySelector('#candidatesBadge');
            var btnAdoptAll = page.querySelector('#btnAdoptAll');
            if (!container || !badge) return;

            container.innerHTML = '';
            var list = safeParseArray(candidates);
            badge.innerText = list.length;

            if (btnAdoptAll) {
                btnAdoptAll.style.display = list.length > 0 ? 'inline-block' : 'none';
            }

            if (list.length === 0) {
                container.innerHTML = '<div style="opacity: 0.7; font-size:13px; padding: 4px 0;">暂无等待确认的疑似同系列。点击右上角“🔍 探查疑似同系列”可立即扫描。</div>';
                return;
            }

            list.forEach(function (group) {
                var card = document.createElement('div');
                card.className = 'paperList';
                card.style.border = '1px solid rgba(255, 255, 255, 0.12)';
                card.style.borderRadius = '6px';
                card.style.padding = '12px 14px';
                card.style.display = 'flex';
                card.style.flexDirection = 'column';
                card.style.gap = '10px';
                card.style.width = '100%';
                card.style.boxSizing = 'border-box';

                var items = group.Items || [];

                var isExisting = !!group.IsExistingCollection;
                var opBadgeHtml = isExisting ?
                    '<span style="font-size:11px; background:#1976d2; color:#fff; padding:2px 8px; border-radius:10px; font-weight:600;">📥 加入现有合集</span>' :
                    '<span style="font-size:11px; background:#2e7d32; color:#fff; padding:2px 8px; border-radius:10px; font-weight:600;">✨ 创建新合集</span>';

                var adoptBtnText = isExisting ? '📥 同步至合集' : '✨ 采纳建集';

                // 顶栏单行：合集名称输入框 | 状态标识 | 勾选统计 | 悬浮添加影片 | 采纳与忽略按钮
                var topRow = document.createElement('div');
                topRow.style.display = 'flex';
                topRow.style.alignItems = 'center';
                topRow.style.justifyContent = 'space-between';
                topRow.style.flexWrap = 'wrap';
                topRow.style.gap = '8px';
                topRow.style.width = '100%';

                topRow.innerHTML =
                    '<div style="display:flex; align-items:center; gap:8px; flex:1; min-width:280px; flex-wrap:wrap;">' +
                    '  <span style="font-size:13px; font-weight:600;">合集名称:</span>' +
                    '  <input is="emby-input" type="text" class="candidate-name" style="flex:1; max-width:240px; font-size:13px;" value="' + (group.SuggestedName || '') + '" />' +
                    opBadgeHtml +
                    '  <span class="selected-count-label" style="font-size:12px; opacity:0.85; background:rgba(255,255,255,0.08); padding:2px 8px; border-radius:10px;">' + items.length + ' / ' + items.length + ' 部已选</span>' +
                    '  <button is="emby-button" type="button" class="btn-search-add raised" style="font-size:11px; padding:2px 8px; background:rgba(255,255,255,0.1); opacity:0; transition:opacity 0.2s ease;">+ 添加影片</button>' +
                    '</div>' +
                    '<div style="display:flex; gap:8px; align-items:center;">' +
                    '  <button is="emby-button" type="button" class="btn-adopt raised button-submit" style="font-size:12px; padding:4px 14px;">' + adoptBtnText + '</button>' +
                    '  <button is="emby-button" type="button" class="btn-ignore raised" style="font-size:12px; padding:4px 10px; opacity:0.75;">忽略</button>' +
                    '</div>';

                // 鼠标移入卡片时显现 [+ 添加影片] 按钮
                var btnSearchAdd = topRow.querySelector('.btn-search-add');
                card.addEventListener('mouseenter', function () { btnSearchAdd.style.opacity = '1'; });
                card.addEventListener('mouseleave', function () { btnSearchAdd.style.opacity = '0'; });

                // 下方直接外露的影片流式胶囊展示区（物理样式锁，彻底杜绝被 Emby 全局 CSS 撑满 100% 宽度）
                var chipsContainer = document.createElement('div');
                chipsContainer.setAttribute('style', 'display: flex !important; flex-direction: row !important; flex-wrap: wrap !important; gap: 8px !important; align-items: center !important; width: 100% !important; box-sizing: border-box !important; padding-top: 6px !important;');

                items.forEach(function (item) {
                    var chip = document.createElement('label');
                    chip.className = 'candidate-chip';
                    var isNew = !!item.IsNewItem;
                    var borderStyle = isNew ? 'border: 1px solid #4caf50 !important;' : 'border: 1px solid rgba(255, 255, 255, 0.18) !important;';
                    var bgStyle = isNew ? 'background: rgba(76, 175, 80, 0.15) !important;' : 'background: rgba(255, 255, 255, 0.08) !important;';

                    chip.setAttribute('style', 'display: inline-flex !important; flex-direction: row !important; align-items: center !important; gap: 6px !important; width: auto !important; max-width: max-content !important; flex: 0 0 auto !important; padding: 4px 10px !important; border-radius: 16px !important; ' + bgStyle + ' ' + borderStyle + ' cursor: pointer !important; user-select: none !important; margin: 0 !important; box-sizing: border-box !important;');

                    var newTagHtml = isNew ? ' <span style="font-size:10px; background:#4caf50; color:#fff; padding:1px 4px; border-radius:4px; margin-left:2px;">新入库</span>' : '';

                    chip.innerHTML =
                        '<input type="checkbox" class="chk-candidate-item" data-id="' + item.Id + '" data-name="' + (item.Name || '').replace(/"/g, '&quot;') + '" checked style="cursor:pointer; margin:0 !important; vertical-align:middle; width:14px !important; height:14px !important;" />' +
                        '<span class="chip-text" style="font-size:12px; white-space:nowrap; vertical-align:middle;">🎬 ' + item.Name + newTagHtml + '</span>';

                    var chk = chip.querySelector('input');
                    var span = chip.querySelector('.chip-text');

                    function updateChipVisual() {
                        if (chk.checked) {
                            chip.style.setProperty('background', isNew ? 'rgba(76, 175, 80, 0.15)' : 'rgba(255, 255, 255, 0.08)', 'important');
                            chip.style.setProperty('border-color', isNew ? '#4caf50' : 'rgba(255, 255, 255, 0.22)', 'important');
                            chip.style.setProperty('opacity', '1', 'important');
                            span.style.textDecoration = 'none';
                        } else {
                            chip.style.setProperty('background', 'transparent', 'important');
                            chip.style.setProperty('border-color', 'rgba(255, 255, 255, 0.06)', 'important');
                            chip.style.setProperty('opacity', '0.4', 'important');
                            span.style.textDecoration = 'line-through';
                        }
                    }

                    chk.addEventListener('change', function () {
                        updateChipVisual();
                        updateCountLabel();
                    });

                    chipsContainer.appendChild(chip);
                });

                // 快捷搜索添加影片逻辑
                btnSearchAdd.addEventListener('click', function () {
                    var searchTerm = prompt("请输入要添加到【" + (topRow.querySelector('.candidate-name').value || '此合集') + "】的影片名称关键词：");
                    if (!searchTerm || !searchTerm.trim()) return;

                    Dashboard.showLoadingMsg();
                    var searchUrl = ApiClient.getUrl('Items', {
                        SearchTerm: searchTerm.trim(),
                        IncludeItemTypes: 'Movie,Series',
                        Recursive: true,
                        Limit: 10
                    });

                    var req = ApiClient.getJSON ? ApiClient.getJSON(searchUrl) : fetch(searchUrl).then(function (r) { return r.json(); });
                    Promise.resolve(req).then(function (res) {
                        Dashboard.hideLoadingMsg();
                        var foundItems = (res && res.Items) ? res.Items : [];
                        if (foundItems.length === 0) {
                            Dashboard.alert("未在服务器中搜索到包含【" + searchTerm + "】的电影或剧集！");
                            return;
                        }

                        // 若搜索到单个或多个结果，直接选第一个或弹出提示添加
                        var targetItem = foundItems[0];
                        if (foundItems.length > 1) {
                            var promptMsg = "搜索到多部作品，请输入对应序号添加：\n\n";
                            foundItems.forEach(function (it, idx) {
                                promptMsg += (idx + 1) + ". " + it.Name + " (" + (it.ProductionYear || '未知年份') + " · " + (it.Type === 'Series' ? '剧集' : '电影') + ")\n";
                            });
                            var chosenIdx = prompt(promptMsg, "1");
                            var num = parseInt(chosenIdx, 10);
                            if (num >= 1 && num <= foundItems.length) {
                                targetItem = foundItems[num - 1];
                            } else {
                                return;
                            }
                        }

                        // 检查是否已经添加
                        var existingIdChk = chipsContainer.querySelector('.chk-candidate-item[data-id="' + targetItem.Id + '"]');
                        if (existingIdChk) {
                            existingIdChk.checked = true;
                            existingIdChk.dispatchEvent(new Event('change'));
                            Dashboard.alert("该影片已在列表中，已自动为您勾选！");
                            return;
                        }

                        // 新建胶囊追加进列表
                        var newChip = document.createElement('label');
                        newChip.className = 'candidate-chip';
                        newChip.setAttribute('style', 'display: inline-flex !important; flex-direction: row !important; align-items: center !important; gap: 6px !important; width: auto !important; max-width: max-content !important; flex: 0 0 auto !important; padding: 4px 10px !important; border-radius: 16px !important; background: rgba(33, 150, 243, 0.2) !important; border: 1px solid #2196f3 !important; cursor: pointer !important; user-select: none !important; margin: 0 !important; box-sizing: border-box !important;');
                        newChip.innerHTML =
                            '<input type="checkbox" class="chk-candidate-item" data-id="' + targetItem.Id + '" data-name="' + (targetItem.Name || '').replace(/"/g, '&quot;') + '" checked style="cursor:pointer; margin:0 !important; vertical-align:middle; width:14px !important; height:14px !important;" />' +
                            '<span class="chip-text" style="font-size:12px; white-space:nowrap; vertical-align:middle;">🎬 ' + targetItem.Name + ' <span style="font-size:10px; background:#2196f3; color:#fff; padding:1px 4px; border-radius:4px;">手动添加</span></span>';

                        var chk = newChip.querySelector('input');
                        var span = newChip.querySelector('.chip-text');
                        chk.addEventListener('change', function () {
                            if (chk.checked) {
                                newChip.style.setProperty('background', 'rgba(33, 150, 243, 0.2)', 'important');
                                newChip.style.setProperty('border-color', '#2196f3', 'important');
                                newChip.style.setProperty('opacity', '1', 'important');
                                span.style.textDecoration = 'none';
                            } else {
                                newChip.style.setProperty('background', 'transparent', 'important');
                                newChip.style.setProperty('border-color', 'rgba(255, 255, 255, 0.06)', 'important');
                                newChip.style.setProperty('opacity', '0.4', 'important');
                                span.style.textDecoration = 'line-through';
                            }
                            updateCountLabel();
                        });

                        chipsContainer.appendChild(newChip);
                        items.push({ Id: targetItem.Id, Name: targetItem.Name, Type: targetItem.Type });
                        updateCountLabel();
                        Dashboard.alert("🎉 已成功将【" + targetItem.Name + "】追加到候选列表！点击采纳后即可一同创建/同步。");
                    }).catch(function (err) {
                        Dashboard.hideLoadingMsg();
                        Dashboard.alert("搜索失败: " + (err.message || err));
                    });
                });

                card.appendChild(topRow);
                card.appendChild(chipsContainer);

                var countLabel = topRow.querySelector('.selected-count-label');
                function updateCountLabel() {
                    var checkedCount = chipsContainer.querySelectorAll('.chk-candidate-item:checked').length;
                    countLabel.innerText = checkedCount + ' / ' + items.length + ' 部已选';
                }

                // 绑定采纳
                topRow.querySelector('.btn-adopt').addEventListener('click', function () {
                    var targetName = topRow.querySelector('.candidate-name').value.trim();
                    if (!targetName) {
                        Dashboard.alert("请输入合集名称！");
                        return;
                    }

                    var selectedCheckboxes = chipsContainer.querySelectorAll('.chk-candidate-item:checked');
                    var selectedIds = [];
                    selectedCheckboxes.forEach(function (chk) {
                        selectedIds.push(parseInt(chk.getAttribute('data-id'), 10));
                    });

                    if (selectedIds.length === 0) {
                        Dashboard.alert("请至少保留 1 部作品加入合集！");
                        return;
                    }

                    Dashboard.showLoadingMsg();
                    var url = ApiClient.getUrl('CrossLibraryCollections/AdoptCandidate');
                    var payload = {
                        CandidateId: group.Id || '',
                        SuggestedName: group.SuggestedName || '',
                        CollectionName: targetName,
                        ItemIds: selectedIds
                    };

                    var req = ApiClient.ajax ?
                        ApiClient.ajax({ type: 'POST', url: url, data: JSON.stringify(payload), contentType: 'application/json' }) :
                        fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });

                    Promise.resolve(req).then(function () {
                        Dashboard.hideLoadingMsg();
                        card.remove();
                        var remain = container.querySelectorAll('.candidate-name').length;
                        badge.innerText = remain;
                        Dashboard.alert("🎉 已成功创建/同步【" + targetName + "】合集并沉淀为规则！可在 Emby 合集库中查看。");
                    }).catch(function (err) {
                        Dashboard.hideLoadingMsg();
                        Dashboard.alert("采纳建集失败: " + (err.message || err));
                    });
                });

                // 绑定忽略
                topRow.querySelector('.btn-ignore').addEventListener('click', function () {
                    Dashboard.showLoadingMsg();
                    var url = ApiClient.getUrl('CrossLibraryCollections/IgnoreCandidate');
                    var payload = {
                        CandidateId: group.Id || '',
                        SuggestedName: group.SuggestedName || ''
                    };

                    var req = ApiClient.ajax ?
                        ApiClient.ajax({ type: 'POST', url: url, data: JSON.stringify(payload), contentType: 'application/json' }) :
                        fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });

                    Promise.resolve(req).then(function () {
                        Dashboard.hideLoadingMsg();
                        card.remove();
                        badge.innerText = container.querySelectorAll('.candidate-name').length;
                    }).catch(function () {
                        Dashboard.hideLoadingMsg();
                        card.remove();
                    });
                });

                container.appendChild(card);
            });
        }

        function refreshCandidatesList() {
            var url = ApiClient.getUrl('CrossLibraryCollections/Candidates');
            var req = ApiClient.getJSON ? ApiClient.getJSON(url) : fetch(url).then(function (r) { return r.json(); });
            return Promise.resolve(req).then(function (list) {
                var safeList = safeParseArray(list);
                renderCandidates(safeList);
                return safeList;
            });
        }

        function checkPluginVersion() {
            var badge = page.querySelector('#pluginVersionBadge');
            var url = ApiClient.getUrl('CrossLibraryCollections/Version');
            var req = ApiClient.getJSON ? ApiClient.getJSON(url) : fetch(url).then(function (r) { return r.json(); });
            Promise.resolve(req).then(function (ver) {
                if (badge && ver) {
                    badge.innerText = 'v' + ver;
                }
            }).catch(function () {});
        }

        function renderRules(rules) {
            var container = page.querySelector('#rulesListContainer');
            var badge = page.querySelector('#rulesBadge');
            if (!container) return;

            container.innerHTML = '';
            var list = rules || [];
            if (badge) badge.innerText = list.length;

            if (list.length === 0) {
                container.innerHTML = '<div style="opacity: 0.6; font-size:12px; padding: 6px 0;">暂无自定义别名规则。普通同前缀系列由算法自动处理，仅在需要聚合特殊跨IP宇宙（如漫威电影宇宙）时在此添加。</div>';
                return;
            }

            list.forEach(function (rule) {
                var div = document.createElement('div');
                div.setAttribute('style', 'display: flex; gap: 8px; margin-bottom: 6px; align-items: center; width: 100%; box-sizing: border-box;');

                div.innerHTML =
                    '<div style="flex:1; display:flex; align-items:center;">' +
                    '<input is="emby-input" type="text" class="rule-name" placeholder="合集名称 (如: 漫威电影宇宙)" value="' + (rule.CollectionName || '') + '" style="margin:0 !important; width:100%; box-sizing:border-box;" />' +
                    '</div>' +
                    '<div style="flex:2; display:flex; align-items:center;">' +
                    '<input is="emby-input" type="text" class="rule-keywords" placeholder="包含关键词 (逗号分隔，如: 钢铁侠,复仇者联盟,洛基)" value="' + (rule.Keywords || '') + '" style="margin:0 !important; width:100%; box-sizing:border-box;" />' +
                    '</div>' +
                    '<button type="button" class="btn-remove-rule" style="display:inline-flex; align-items:center; justify-content:center; padding:4px 12px; font-size:12px; background:rgba(204,51,51,0.15); border:1px solid rgba(204,51,51,0.4); color:#ff5252; border-radius:4px; cursor:pointer; height:auto; margin:0; flex-shrink:0;">删除</button>';

                div.querySelector('.btn-remove-rule').addEventListener('click', function () {
                    div.remove();
                    if (badge) badge.innerText = container.querySelectorAll('.rule-name').length;
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
                    container.innerHTML = '<span style="opacity: 0.7;">未发现可供排除的媒体库</span>';
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

            checkPluginVersion();

            // 1. 读取基础配置
            ApiClient.getPluginConfiguration(pluginId).then(function (config) {
                page.querySelector('#chkEnableTask').checked = config.EnableScheduledTask;
                page.querySelector('#chkAutoClustering').checked = config.EnableAutoClustering;
                page.querySelector('#chkReverseCleanup').checked = config.EnableReverseCleanup;
                page.querySelector('#chkAutoSetPoster').checked = config.AutoSetPoster !== false;
                page.querySelector('#txtMinItems').value = config.MinItemsForCollection || 2;
                page.querySelector('#txtDelimiterWords').value = config.DelimiterWords || '';
                page.querySelector('#txtNoiseAndModifiers').value = config.NoiseAndModifiers || '';
                page.querySelector('#txtBlacklist').value = config.BlacklistKeywords || '';
                page.querySelector('#txtTmdbKey').value = config.TmdbApiKey || '';

                renderRules(config.CustomRules || []);
                loadVirtualFolders(config.ExcludedLibraryIds || []);

                // 2. 通过专用端点读取最新的候选列表（保证数据实时性与独立性）
                refreshCandidatesList().then(function () {
                    Dashboard.hideLoadingMsg();
                }).catch(function () {
                    Dashboard.hideLoadingMsg();
                });
            });

            // 监听候选推荐折叠展开状态变化
            var candDetails = page.querySelector('#candidatesDetails');
            var candStatus = page.querySelector('#candidatesToggleStatus');
            if (candDetails && candStatus) {
                function updateCandStatus() {
                    candStatus.innerText = candDetails.open ? '收起列表 ▴' : '展开列表 ▾';
                }
                candDetails.addEventListener('toggle', updateCandStatus);
                updateCandStatus();
            }

            // 监听规则列表收起/展开
            var btnToggleRules = page.querySelector('#btnToggleRules');
            var rulesList = page.querySelector('#rulesListContainer');
            var rulesToggleLabel = page.querySelector('#rulesToggleLabel');
            if (btnToggleRules && rulesList && rulesToggleLabel) {
                btnToggleRules.addEventListener('click', function () {
                    var isHidden = rulesList.style.display === 'none';
                    rulesList.style.display = isHidden ? 'block' : 'none';
                    rulesToggleLabel.innerText = isHidden ? '收起列表 ▴' : '展开列表 ▾';
                });
            }
        });

        // 快速探查疑似候选：执行完成后强制调用 GET /Candidates 重新装载，消除数据格式解析差异
        page.querySelector('#btnFastDiscover').addEventListener('click', function () {
            Dashboard.showLoadingMsg();
            var url = ApiClient.getUrl('CrossLibraryCollections/DiscoverCandidates');

            var postReq = ApiClient.ajax ?
                ApiClient.ajax({ type: 'POST', url: url }) :
                fetch(url, { method: 'POST' });

            Promise.resolve(postReq).then(function () {
                // 探查命令执行完毕后，统一通过成熟可靠的 GET 接口拉取全量候选
                return refreshCandidatesList();
            }).then(function (list) {
                Dashboard.hideLoadingMsg();
                var details = page.querySelector('#candidatesDetails');
                if (details) details.open = true; // 探查完自动展开供审核
                Dashboard.alert("🎉 探查完成！当前共有 " + list.length + " 组疑似同系列候选。");
            }).catch(function (err) {
                Dashboard.hideLoadingMsg();
                Dashboard.alert("探查请求失败: " + (err.message || err));
            });
        });

        // 一键采纳全部候选
        page.querySelector('#btnAdoptAll').addEventListener('click', function () {
            var cards = page.querySelectorAll('#candidatesListContainer > .paperList');
            if (cards.length === 0) return;

            var confirmed = confirm("确定要一键采纳当前显示的全部 " + cards.length + " 组候选系列吗？\n\n系统将按照卡片上当前修改后的合集名和勾选的作品批量创建合集并沉淀为规则。");
            if (!confirmed) return;

            Dashboard.showLoadingMsg();

            var batchItems = [];
            cards.forEach(function (card) {
                var nameInput = card.querySelector('.candidate-name');
                var targetName = nameInput ? nameInput.value.trim() : '';
                if (!targetName) return;

                var selectedIds = [];
                card.querySelectorAll('.chk-candidate-item:checked').forEach(function (chk) {
                    selectedIds.push(parseInt(chk.getAttribute('data-id'), 10));
                });

                if (selectedIds.length > 0) {
                    batchItems.push({
                        CandidateId: '',
                        SuggestedName: targetName,
                        CollectionName: targetName,
                        ItemIds: selectedIds
                    });
                }
            });

            var url = ApiClient.getUrl('CrossLibraryCollections/AdoptAllCandidates');
            var req = ApiClient.ajax ?
                ApiClient.ajax({ type: 'POST', url: url, data: JSON.stringify({ Candidates: batchItems }), contentType: 'application/json' }) :
                fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ Candidates: batchItems }) });

            Promise.resolve(req).then(function () {
                return refreshCandidatesList();
            }).then(function () {
                Dashboard.hideLoadingMsg();
                Dashboard.alert("🎉 批量采纳完成！所有合集已创建并沉淀规则。可在 Emby 合集库中查看。");
            }).catch(function (err) {
                Dashboard.hideLoadingMsg();
                Dashboard.alert("批量采纳失败: " + (err.message || err));
            });
        });

        page.querySelector('#btnAddRule').addEventListener('click', function () {
            var container = page.querySelector('#rulesListContainer');
            var div = document.createElement('div');
            div.setAttribute('style', 'display: flex; gap: 8px; margin-bottom: 6px; align-items: center; width: 100%; box-sizing: border-box;');

            div.innerHTML =
                '<div style="flex:1; display:flex; align-items:center;">' +
                '<input is="emby-input" type="text" class="rule-name" placeholder="合集名称 (如: 漫威电影宇宙)" style="margin:0 !important; width:100%; box-sizing:border-box;" />' +
                '</div>' +
                '<div style="flex:2; display:flex; align-items:center;">' +
                '<input is="emby-input" type="text" class="rule-keywords" placeholder="包含关键词 (如: 复仇者联盟,钢铁侠,洛基,旺达幻视)" style="margin:0 !important; width:100%; box-sizing:border-box;" />' +
                '</div>' +
                '<button type="button" class="btn-remove-rule" style="display:inline-flex; align-items:center; justify-content:center; padding:4px 12px; font-size:12px; background:rgba(204,51,51,0.15); border:1px solid rgba(204,51,51,0.4); color:#ff5252; border-radius:4px; cursor:pointer; height:auto; margin:0; flex-shrink:0;">删除</button>';

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
                config.DelimiterWords = page.querySelector('#txtDelimiterWords').value;
                config.NoiseAndModifiers = page.querySelector('#txtNoiseAndModifiers').value;
                config.BlacklistKeywords = page.querySelector('#txtBlacklist').value;
                config.TmdbApiKey = page.querySelector('#txtTmdbKey').value;

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

                // 核心安全锁：绝不允许通过普通表单提交覆盖清空服务端的候选推荐池与忽略列表
                delete config.PendingCandidates;
                delete config.IgnoredCandidatePrefixes;

                ApiClient.updatePluginConfiguration(pluginId, config).then(function (result) {
                    Dashboard.processPluginConfigurationUpdateResult(result);
                });
            });

            return false;
        });

        // 独立补全所有合集封面
        page.querySelector('#btnRefreshPosters').addEventListener('click', function () {
            Dashboard.showLoadingMsg();
            var url = ApiClient.getUrl('CrossLibraryCollections/RefreshPosters');

            var req = ApiClient.ajax ? ApiClient.ajax({ type: 'POST', url: url }) : fetch(url, { method: 'POST' });
            Promise.resolve(req).then(function () {
                Dashboard.hideLoadingMsg();
                Dashboard.alert("🎨 全服合集封面深度扫描与补全任务已在后台启动！\n\n系统将按照【TMDb官方 > 本地电视剧 > 本地电影】的最高优先级穿透提取高清海报。稍后可在 Emby 合集库中查看效果。");
            }).catch(function (err) {
                Dashboard.hideLoadingMsg();
                Dashboard.alert("启动封面补全任务失败: " + (err.message || err));
            });
        });

        // 清空并全量重新扫描创建
        page.querySelector('#btnRebuild').addEventListener('click', function () {
            var confirmed = confirm("⚠️ 警告：确定要清空由本插件管理的合集并全量重新扫描创建吗？\n\n该操作会立即在服务端后台执行全量聚类并生成最新疑似候选推荐，日常定时任务则保持平滑增量。");
            if (!confirmed) return;

            Dashboard.showLoadingMsg();
            var rebuildUrl = ApiClient.getUrl('CrossLibraryCollections/Rebuild');

            var req = ApiClient.ajax ? ApiClient.ajax({ type: 'POST', url: rebuildUrl }) : fetch(rebuildUrl, { method: 'POST' });
            Promise.resolve(req).then(function () {
                Dashboard.hideLoadingMsg();
                Dashboard.alert("🚀 全量清空与重建任务已在服务端后台启动！\n\n稍后刷新本页面即可查看新探查出的疑似候选系列。");
            }).catch(function (err) {
                Dashboard.hideLoadingMsg();
                Dashboard.alert("启动重建任务失败: " + (err.message || err));
            });
        });
    };
});

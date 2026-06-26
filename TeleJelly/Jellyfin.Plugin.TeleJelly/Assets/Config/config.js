const LinkPrefix = "l:";

const tgConfigPage = {
    pluginUniqueId: "4b71013d-00ba-470c-9e4d-0c451a435328",
    autoRefreshIntervalMs: 5000,

    // Track modified groups separately from loaded config
    modifiedGroups: new Map(),
    currentGroup: null,


    /** ======== ======== GENERAL CONFIG ======== ======== */


    loadConfiguration: (page) => {
        ApiClient.getPluginConfiguration(tgConfigPage.pluginUniqueId).then(
            (config) => {
                tgConfigPage.populateConfiguration(page, config);
                tgConfigPage.populateDownloadManagerConfig(page, config);
                tgConfigPage.populateGroups(page, config);
            }
        );
    },

    populateConfiguration: (page, config) => {
        if (config.BotToken) {
            tgTokenHelper.validateToken(page, config.BotToken);
        }

        const botUserName = config.BotUsername || tgTokenHelper.currentUserName;

        // Set basic config values
        page.querySelector("#TgBotToken").value = config.BotToken || tgTokenHelper.currentToken;
        page.querySelector("#TgBotUsername").innerHTML = botUserName;
        page.querySelector("#LoginBaseUrl").value = config.LoginBaseUrl ?? '';
        page.querySelector("#TgAdministrators").value = config.AdminUserNames?.join("\r\n") || "";
        page.querySelector("#ForcedUrlScheme").value = config.ForcedUrlScheme || "none";
        page.querySelector("#EnableBotService").checked = config.EnableBotService ?? true;
        page.querySelector("#EnableInlineQueries").checked = config.EnableInlineQueries ?? false;

        // Update Telegram Login-Page URL (use LoginBaseUrl if set, otherwise current origin)
        tgConfigPage.updateLoginUrl(page, config.LoginBaseUrl);
    },

    populateDownloadManagerConfig: (page, config) => {
        const textarea = page.querySelector("#DownloadManagerConfigJson");
        if (!textarea) {
            return;
        }

        textarea.value = JSON.stringify(config.DownloadManager || {}, null, 2);
    },

    parseDownloadManagerConfig: (page) => {
        const textarea = page.querySelector("#DownloadManagerConfigJson");
        if (!textarea) {
            return {};
        }

        const raw = (textarea.value || "").trim();
        if (!raw.length) {
            return {};
        }

        try {
            const parsed = JSON.parse(raw);
            if (!parsed || Array.isArray(parsed) || typeof parsed !== "object") {
                throw new Error("the value must be a JSON object");
            }

            return parsed;
        } catch (error) {
            window.Dashboard.alert(`Download Manager JSON is invalid: ${error.message || error}`);
            return null;
        }
    },

    formatDownloadManagerConfig: (page) => {
        const parsed = tgConfigPage.parseDownloadManagerConfig(page);
        if (parsed === null) {
            return;
        }

        page.querySelector("#DownloadManagerConfigJson").value = JSON.stringify(parsed, null, 2);
    },

    reloadDownloadManagerConfig: (page) => {
        ApiClient.getPluginConfiguration(tgConfigPage.pluginUniqueId).then((config) => {
            tgConfigPage.populateDownloadManagerConfig(page, config);
            window.Dashboard.alert("Download manager JSON reloaded from saved configuration.");
        });
    },

    updateLoginUrl: (page, loginBaseUrl) => {
        const baseUrl = loginBaseUrl && loginBaseUrl.trim().length > 0
            ? loginBaseUrl.trim()
            : window.location.origin;
        const loginUrl = `${baseUrl}/sso/Telegram`;

        page.querySelector("#SSOTelegramLoginUrl").href = loginUrl;
        page.querySelector("#SSOTelegramLoginUrl").innerText = loginUrl;

        // Also update the branding widget
        const brandingWidget = `
<form action="${loginUrl}">
<button is="emby-button" style="display:flex;flex-direction:row;width:auto;" class="block emby-button raised button-submit">
Sign in with Telegram
<svg viewBox="0 0 240 240" xmlns="http://www.w3.org/2000/svg" style="max-height:4.20em;">
    <defs>
        <linearGradient gradientUnits="userSpaceOnUse" x2="120" y1="240" x1="120" id="linear-gradient">
            <stop stop-color="#1d93d2" offset="0"></stop>
            <stop stop-color="#38b0e3" offset="1"></stop>
        </linearGradient>
    </defs>
    <title>Telegram_logo</title>
    <circle fill="url(#linear-gradient)" r="120" cy="120" cx="120"></circle>
    <path fill="#fff" d="M81.486,130.178,52.2,120.636s-3.5-1.42-2.373-4.64c.232-.664.7-1.229,2.1-2.2,6.489-4.523,120.106-45.36,120.106-45.36s3.208-1.081,5.1-.362a2.766,2.766,0,0,1,1.885,2.055,9.357,9.357,0,0,1,.254,2.585c-.009.752-.1,1.449-.169,2.542-.692,11.165-21.4,94.493-21.4,94.493s-1.239,4.876-5.678,5.043A8.13,8.13,0,0,1,146.1,172.5c-8.711-7.493-38.819-27.727-45.472-32.177a1.27,1.27,0,0,1-.546-.9c-.093-.469.417-1.05.417-1.05s52.426-46.6,53.821-51.492c.108-.379-.3-.566-.848-.4-3.482,1.281-63.844,39.4-70.506,43.607A3.21,3.21,0,0,1,81.486,130.178Z"></path>
</svg>
</button>
</form>`;

        page.querySelector("#ExampleBranding").innerHTML = brandingWidget;
        page.querySelector("#ExampleBrandingCode").innerHTML = brandingWidget.replace(/>/g, "&gt;").replace(/</g, "&lt;").replace(/"/g, "&quot;");
    },


    populateGroups: (page, config) => {
        // Populate group list
        const groupList = page.querySelector("#TgBotGroupList");
        groupList.innerHTML = ''; // Clear existing groups

        console.debug("Populating groups (cleared)");

        config.TelegramGroups?.forEach((group) => {
            console.debug(`Populating Group ${group}`);

            const groupItem = document.createElement('div');
            groupItem.className = 'group-item';
            groupItem.setAttribute('data-group-name', group.GroupName);
            groupItem.textContent = group.GroupName;
            groupItem.addEventListener('click', () => tgConfigPage.selectGroup(page, group.GroupName));
            groupList.appendChild(groupItem);
        });

        // If we had a selected group, try to reselect it
        if (tgConfigPage.currentGroup) {
            tgConfigPage.selectGroup(page, tgConfigPage.currentGroup);
        } else {
            tgConfigPage.updateGroupEditingState(page);
        }
    },

    /** ======== ======== DOWNLOAD MANAGER ======== ======== */
    loadDownloads: (page) => {
        const selectedStatus = page.querySelector("#DownloadStatusFilter")?.value || "All";
        const query = selectedStatus && selectedStatus !== "All"
            ? `?status=${encodeURIComponent(selectedStatus)}`
            : "";

        window.ApiClient.ajax({
            url: window.ApiClient.getUrl(`/TeleJelly/DownloadManager/downloads${query}`),
            type: "GET",
            dataType: "json"
        }).then((downloads) => {
            tgConfigPage.populateDownloads(page, downloads);
        });

        window.ApiClient.ajax({
            url: window.ApiClient.getUrl("/TeleJelly/DownloadManager/health"),
            type: "GET",
            dataType: "json"
        }).then((health) => {
            tgConfigPage.populateServiceHealth(page, health);
        });
    },

    loadDownloadLogs: (page) => {
        window.ApiClient.ajax({
            url: window.ApiClient.getUrl("/TeleJelly/DownloadManager/logs?limit=200"),
            type: "GET",
            dataType: "json"
        }).then((logs) => {
            tgConfigPage.populateDownloadLogs(page, logs);
        });
    },

    populateDownloads: (page, downloads) => {
        const listContainer = page.querySelector("#DownloadList");
        listContainer.innerHTML = "";

        if (!downloads || downloads.length === 0) {
            listContainer.innerHTML = '<div class="listItem">No active downloads.</div>';
            return;
        }

        const header = document.createElement("div");
        header.className = "listItem listItem-border";
        header.style.display = "grid";
        header.style.gridTemplateColumns = "2fr 1fr 0.7fr 1fr 1fr 1.4fr";
        header.style.fontWeight = "bold";
        header.style.padding = "0.5em";
        header.innerHTML = "<div>Title</div><div>Status</div><div>Progress</div><div>Service</div><div>Started</div><div>Actions</div>";
        listContainer.appendChild(header);

        downloads.forEach(dl => {
            const item = document.createElement("div");
            item.className = "listItem listItem-border";
            item.style.display = "grid";
            item.style.gridTemplateColumns = "2fr 1fr 0.7fr 1fr 1fr 1.4fr";
            item.style.alignItems = "center";
            item.style.padding = "0.5em";

            const title = document.createElement("div");
            title.textContent = `${dl.Title} (${dl.Year || "?"})`;
            const status = document.createElement("div");
            status.textContent = dl.Status;
            const progress = document.createElement("div");
            progress.textContent = `${dl.ProgressPercentage.toFixed(1)}%`;
            const service = document.createElement("div");
            service.textContent = dl.ServiceName || "n/a";
            const started = document.createElement("div");
            started.textContent = new Date(dl.StartedAt).toLocaleString();
            const actions = document.createElement("div");
            actions.style.display = "flex";
            actions.style.gap = "0.5em";
            actions.style.flexWrap = "wrap";

            if (["Downloading", "Extracting", "Analyzing", "Organizing"].includes(dl.Status)) {
                const cancelBtn = document.createElement("button");
                cancelBtn.is = "emby-button";
                cancelBtn.type = "button";
                cancelBtn.className = "raised button-cancel emby-button";
                cancelBtn.textContent = "Cancel";
                cancelBtn.onclick = () => tgConfigPage.cancelDownload(page, dl.Id);
                actions.appendChild(cancelBtn);
            } else {
                const retryBtn = document.createElement("button");
                retryBtn.is = "emby-button";
                retryBtn.type = "button";
                retryBtn.className = "raised button-submit emby-button";
                retryBtn.textContent = "Retry";
                retryBtn.onclick = () => tgConfigPage.retryDownload(page, dl.Id);
                actions.appendChild(retryBtn);
            }

            const removeBtn = document.createElement("button");
            removeBtn.is = "emby-button";
            removeBtn.type = "button";
            removeBtn.className = "raised button-alt emby-button";
            removeBtn.textContent = "Remove";
            removeBtn.onclick = () => tgConfigPage.removeDownload(page, dl.Id, false);
            actions.appendChild(removeBtn);

            const cleanBtn = document.createElement("button");
            cleanBtn.is = "emby-button";
            cleanBtn.type = "button";
            cleanBtn.className = "raised button-alt emby-button";
            cleanBtn.textContent = "Clean Files";
            cleanBtn.onclick = () => tgConfigPage.removeDownload(page, dl.Id, true);
            actions.appendChild(cleanBtn);

            item.appendChild(title);
            item.appendChild(status);
            item.appendChild(progress);
            item.appendChild(service);
            item.appendChild(started);
            item.appendChild(actions);
            listContainer.appendChild(item);
        });
    },

    populateServiceHealth: (page, healthEntries) => {
        const healthContainer = page.querySelector("#ServiceHealthList");
        if (!healthContainer) return;

        healthContainer.innerHTML = "";
        if (!healthEntries || healthEntries.length === 0) {
            healthContainer.innerHTML = '<div class="listItem">No service health data yet.</div>';
            return;
        }

        healthEntries.forEach(entry => {
            const row = document.createElement("div");
            row.className = "listItem listItem-border";
            row.style.padding = "0.5em";
            row.textContent = `${entry.ServiceName}: ${entry.State} | Failures: ${entry.ConsecutiveFailures} | Last success: ${entry.LastSuccess ? new Date(entry.LastSuccess).toLocaleString() : "never"}`;
            healthContainer.appendChild(row);
        });
    },

    populateDownloadLogs: (page, logs) => {
        const logContainer = page.querySelector("#DownloadManagerLogList");
        if (!logContainer) return;

        logContainer.innerHTML = "";
        if (!logs || logs.length === 0) {
            logContainer.innerHTML = '<div class="listItem">No activity recorded yet.</div>';
            return;
        }

        logs.forEach((log) => {
            const row = document.createElement("div");
            const levelClass = (log.Level || "").toLowerCase();
            row.className = `listItem listItem-border download-log-entry is-${levelClass}`;

            const timestamp = document.createElement("div");
            timestamp.className = "log-timestamp";
            timestamp.textContent = log.TimestampUtc
                ? new Date(log.TimestampUtc).toLocaleString()
                : "Unknown time";

            const level = document.createElement("div");
            level.className = "log-level";
            level.textContent = log.Level || "Info";

            const message = document.createElement("div");
            message.className = "log-message";
            const source = document.createElement("div");
            source.className = "log-source";
            source.textContent = log.Source || "DownloadManager";
            const text = document.createElement("div");
            text.className = "log-message-text";
            text.textContent = log.Message || "";
            message.appendChild(source);
            message.appendChild(text);

            row.appendChild(timestamp);
            row.appendChild(level);
            row.appendChild(message);
            logContainer.appendChild(row);
        });

        logContainer.scrollTop = logContainer.scrollHeight;
    },

    cancelDownload: (page, downloadId) => {
        if (!confirm("Cancel this download?")) return;

        window.ApiClient.ajax({
            url: window.ApiClient.getUrl(`/TeleJelly/DownloadManager/downloads/${downloadId}/cancel`),
            type: "POST"
        }).then(() => {
            tgConfigPage.loadDownloads(page);
            window.Dashboard.alert('Download canceled.');
        });
    },

    retryDownload: (page, downloadId) => {
        window.ApiClient.ajax({
            url: window.ApiClient.getUrl(`/TeleJelly/DownloadManager/downloads/${downloadId}/retry`),
            type: "POST"
        }).then(() => {
            tgConfigPage.loadDownloads(page);
            window.Dashboard.alert('Download retry started.');
        });
    },

    removeDownload: (page, downloadId, deleteFiles) => {
        const prompt = deleteFiles
            ? "Remove this download and delete managed staging files?"
            : "Remove this download record?";
        if (!confirm(prompt)) return;

        const query = deleteFiles ? "?deleteFiles=true" : "";
        window.ApiClient.ajax({
            url: window.ApiClient.getUrl(`/TeleJelly/DownloadManager/downloads/${downloadId}${query}`),
            type: "DELETE"
        }).then(() => {
            tgConfigPage.loadDownloads(page);
            window.Dashboard.alert(deleteFiles ? 'Download and files removed.' : 'Download removed.');
        });
    },


    /** ======== ======== REQUEST MANAGEMENT ======== ======== */

    loadRequests: (page) => {
        window.ApiClient.ajax({
            url: window.ApiClient.getUrl("/api/TeleJellyConfig/GetRequests"),
            type: "GET",
            dataType: "json"
        }).then((requests) => {
            tgConfigPage.populateRequests(page, requests);
        });
    },

    populateRequests: (page, requests) => {
        const listContainer = page.querySelector("#RequestList");
        listContainer.innerHTML = "";

        if (!requests || requests.length === 0) {
            listContainer.innerHTML = '<div class="listItem">No active requests.</div>';
            return;
        }

        requests.forEach(req => {
            const item = document.createElement("div");
            item.className = "listItem listItem-border";
            item.style.display = "flex";
            item.style.alignItems = "center";
            item.style.justifyContent = "space-between";
            item.style.padding = "0.5em";

            const info = document.createElement("div");
            info.style.display = "flex";
            info.style.flexDirection = "column";

            const title = document.createElement("div");
            title.style.fontWeight = "bold";

            // Create clickable IMDB link for the title
            if (req.ImdbId) {
                const titleLink = document.createElement("a");
                titleLink.href = `https://www.imdb.com/title/${req.ImdbId}/`;
                titleLink.target = "_blank";
                titleLink.rel = "noopener noreferrer";
                titleLink.textContent = `${req.Title || "Unknown"} (${req.Year || "?"})`;
                titleLink.style.color = "inherit";
                titleLink.style.textDecoration = "underline";
                titleLink.style.textDecorationColor = "rgba(255, 255, 255, 0.5)";
                title.appendChild(titleLink);
            } else {
                title.textContent = `${req.Title || "Unknown"} (${req.Year || "?"})`;
            }

            const details = document.createElement("div");
            details.style.opacity = "0.7";
            details.style.fontSize = "0.9em";
            details.textContent = `IMDb: ${req.ImdbId} | User: ${req.UserDisplayName} | Date: ${new Date(req.RequestedAtUtc).toLocaleDateString()}`;

            info.appendChild(title);
            info.appendChild(details);
            item.appendChild(info);

            const delBtn = document.createElement("button");
            delBtn.is = "emby-button";
            delBtn.type = "button";
            delBtn.className = "raised button-delete emby-button";
            delBtn.textContent = "Remove";
            delBtn.style.marginLeft = "1em";
            delBtn.onclick = () => tgConfigPage.deleteRequest(page, req.ImdbId);

            item.appendChild(delBtn);
            listContainer.appendChild(item);
        });
    },

    deleteRequest: (page, imdbId) => {
        if (!confirm("Remove this request?")) return;

        window.ApiClient.ajax({
            url: window.ApiClient.getUrl(`/api/TeleJellyConfig/RemoveRequest/${encodeURIComponent(imdbId)}`),
            type: "DELETE"
        }).then(() => {
            tgConfigPage.loadRequests(page);
            window.Dashboard.alert('Request removed successfully');
        });
    },

    addRequest: (page) => {
        const input = page.querySelector("#NewRequestImdbId");
        const imdbId = input.value.trim();

        if (!imdbId) return;

        window.ApiClient.ajax({
            url: window.ApiClient.getUrl("/api/TeleJellyConfig/AddRequest"),
            type: "POST",
            data: JSON.stringify({imdbId}),
            contentType: "application/json",
            dataType: "json"
        }).then(() => {
            input.value = "";
            tgConfigPage.loadRequests(page);
            window.Dashboard.alert('Request added successfully');
        }).catch((err) => {
            if (err?.status === 404) {
                window.Dashboard.alert("No metadata found for the provided IMDb ID.");
            } else if (err?.status === 409) {
                window.Dashboard.alert("Request already exists.");
            } else {
                window.Dashboard.alert("Failed to add request.");
            }
        });
    },


    saveConfig: (page) => {
        return new Promise((resolve) => {
            const parsedDownloadManagerConfig = tgConfigPage.parseDownloadManagerConfig(page);
            if (parsedDownloadManagerConfig === null) {
                resolve(false);
                return;
            }

            window.ApiClient.getPluginConfiguration(
                tgConfigPage.pluginUniqueId
            ).then((config) => {
                const baseUrlValue = (page.querySelector("#LoginBaseUrl").value ?? "").trim();
                const finalBaseUrl = baseUrlValue.length ? baseUrlValue : undefined;

                // apply basic config
                config.BotToken = tgTokenHelper.currentToken;
                config.BotUsername = tgTokenHelper.currentUserName;
                config.LoginBaseUrl = finalBaseUrl;
                config.AdminUserNames = tgConfigPage.parseTextList(page.querySelector("#TgAdministrators"));
                config.ForcedUrlScheme = page.querySelector("#ForcedUrlScheme").value || "none";
                config.EnableBotService = page.querySelector("#EnableBotService").checked;
                config.EnableInlineQueries = page.querySelector("#EnableInlineQueries").checked;
                config.DownloadManager = parsedDownloadManagerConfig;

                // save it
                window.ApiClient.updatePluginConfiguration(
                    tgConfigPage.pluginUniqueId,
                    config
                ).then(function (result) {
                    window.Dashboard.processPluginConfigurationUpdateResult(result);
                    tgConfigPage.loadConfiguration(page);
                    resolve(true);
                });
            });
        });
    },


    /** ======== ======== GROUP CONFIG ======== ======== */


    addGroup: (page) => {
        const newGroupName = page.querySelector("#TgGroupName").value.trim();
        if (!newGroupName) {
            window.Dashboard.alert('Please enter a group name');
            return;
        }

        // Validate length
        if (newGroupName.length < 3 || newGroupName.length > 32) {
            window.Dashboard.alert('Group name must be between 3 and 32 characters');
            return;
        }

        // Validate allowed characters using regex
        const validCharsRegex = /^[a-zA-Z0-9_\-]+$/;
        if (!validCharsRegex.test(newGroupName)) {
            window.Dashboard.alert('Group name can only contain letters, numbers, underscore, and hyphen');
            return;
        }
        ApiClient.getPluginConfiguration(tgConfigPage.pluginUniqueId).then((config) => {
            if (!config.TelegramGroups) {
                config.TelegramGroups = [];
            }

            // Check if group already exists
            if (config.TelegramGroups.some(g => g.GroupName === newGroupName)) {
                window.Dashboard.alert('A group with this name already exists');
                return;
            }

            // Add new group
            config.TelegramGroups.push({
                GroupName: newGroupName,
                EnableAllFolders: false,
                EnabledFolders: [],
                LinkedTelegramGroupId: null,
                UserNames: [],
            });

            ApiClient.updatePluginConfiguration(
                tgConfigPage.pluginUniqueId,
                config
            ).then(function (result) {
                window.Dashboard.processPluginConfigurationUpdateResult(result);
                tgConfigPage.currentGroup = newGroupName;
                tgConfigPage.populateGroups(page, config);
                page.querySelector("#TgGroupName").value = ''; // Clear input after adding
            });
        });
    },

    updateGroupEditingState: (page) => {
        const hasSelectedGroup = !!tgConfigPage.currentGroup;
        const enableAllChecked = page.querySelector("#EnableAllFolders")?.checked || false;

        // Elements to toggle
        const userNamesList = page.querySelector("#UserNames");
        const enableAllFolders = page.querySelector("#EnableAllFolders");
        const folderList = page.querySelector("#EnabledFolders");
        const delGroupBtn = page.querySelector("#DeleteGroup");

        // Disable/enable elements based on both group selection AND EnableAllFolders state
        [userNamesList, enableAllFolders, delGroupBtn].forEach(element => {
            if (element) {
                element.disabled = !hasSelectedGroup;
                element.title = hasSelectedGroup ? "" : "Please select or create a group first";
            }
        });

        // Handle folder list checkboxes
        if (folderList) {
            const checkboxes = folderList.querySelectorAll('input[type="checkbox"]');
            checkboxes.forEach(checkbox => {
                // Disable if either no group is selected OR EnableAll is checked
                checkbox.disabled = !hasSelectedGroup || enableAllChecked;
                checkbox.parentElement.title = hasSelectedGroup ? "" : "Please select or create a group first";
            });
        }

        // Visual feedback
        if (userNamesList) {
            userNamesList.style.opacity = hasSelectedGroup ? "1" : "0.6";
        }
        if (folderList) {
            folderList.style.opacity = hasSelectedGroup ? "1" : "0.6";
        }
    },

    // Track changes to currently selected group
    updateGroupData: (page) => {
        if (!tgConfigPage.currentGroup) return;

        console.debug("Updating group data.");

        const linkedText = (page.querySelector("#LinkedTelegramGroupId")?.innerText || "").trim();
        const linkedId = linkedText && linkedText !== "None" ? Number(linkedText) : 0;
        const hasLinkedChat = !!linkedId;

        const groupData = {
            GroupName: tgConfigPage.currentGroup,
            EnableAllFolders: page.querySelector("#EnableAllFolders").checked,
            EnabledFolders: tgConfigPage.serializeEnabledFolders(page),
            UserNames: tgConfigPage.parseTextList(page.querySelector("#UserNames")),
            // Only include TelegramGroupChat if actually linked
            TelegramGroupChat: hasLinkedChat ? {
                TelegramChatId: linkedId,
                SyncUserNames: page.querySelector("#SyncUserNames").checked,
                NotifyNewContent: page.querySelector("#NotifyNewContent").checked,
                AllowRequests: (page.querySelector("#AllowRequests")?.checked) ?? true,
            } : undefined
        };

        tgConfigPage.modifiedGroups.set(tgConfigPage.currentGroup, groupData);
    },

    selectGroup: (page, groupName) => {
        tgConfigPage.currentGroup = groupName;

        console.debug(`Selecting group: ${groupName}.`);

        // set Bot Link-Command Url
        const encodedText = btoa(`${LinkPrefix}${groupName}`);
        page.querySelector("#BotLinkCommandUrl").href = `https://t.me/${tgTokenHelper.currentUserName}?startgroup=${encodedText}`;

        // Update selected state in UI
        page.querySelectorAll('.group-item').forEach(item => {
            item.classList.toggle('selected', item.getAttribute('data-group-name') === groupName);
        });

        // Load group data - first check modified groups, then fall back to config
        let groupData = tgConfigPage.modifiedGroups.get(groupName);

        if (!groupData) {
            ApiClient.getPluginConfiguration(tgConfigPage.pluginUniqueId).then((config) => {
                groupData = config.TelegramGroups?.find(group => group.GroupName === groupName);
                if (groupData) {
                    tgConfigPage.populateGroupData(page, groupData);
                }
            });
        } else {
            tgConfigPage.populateGroupData(page, groupData);
        }

        tgConfigPage.updateGroupEditingState(page);
    },

    populateGroupData: (page, groupData) => {
        if (groupData) {
            // First populate folders
            tgConfigPage.populateEnabledFolders(groupData.EnabledFolders || [], page.querySelector("#EnabledFolders"));

            // Then update their disabled state based on EnableAllFolders
            const folderCheckboxes = page.querySelectorAll('.folder-checkbox');
            folderCheckboxes.forEach(cb => {
                cb.disabled = groupData.EnableAllFolders;
                if (groupData.EnableAllFolders) {
                    cb.checked = true;
                }
            });

            const enableAllCheckbox = page.querySelector("#EnableAllFolders");
            enableAllCheckbox.checked = groupData.EnableAllFolders;

            page.querySelector("#LinkedTelegramGroupId").innerHTML = groupData.TelegramGroupChat?.TelegramChatId ?? "None";
            page.querySelector("#UserNames").value = groupData.UserNames.join("\r\n");
            page.querySelector("#SyncUserNames").checked = groupData.TelegramGroupChat?.SyncUserNames ?? true;
            page.querySelector("#NotifyNewContent").checked = groupData.TelegramGroupChat?.NotifyNewContent ?? true;
            const allowReq = page.querySelector("#AllowRequests");
            if (allowReq) allowReq.checked = groupData.TelegramGroupChat?.AllowRequests ?? true;

            // Toggle Telegram controls based on link state and chat type
            tgConfigPage.updateTelegramSettingsUI(page, groupData);
        }
    },

    updateTelegramSettingsUI: (page, groupData) => {
        const linkedId = groupData.TelegramGroupChat?.TelegramChatId ?? 0;
        const hasLinked = !!linkedId;
        const isModified = tgConfigPage.modifiedGroups.has(groupData.GroupName);

        const notLinkedSection = page.querySelector('#TelegramNotLinkedSection');
        const linkedSection = page.querySelector('#TelegramLinkedSection');
        const sync = page.querySelector('#SyncUserNames');
        const notify = page.querySelector('#NotifyNewContent');
        const allowReq = page.querySelector('#AllowRequests');
        const linkBtn = page.querySelector('#BotLinkCommandUrl');

        // Toggle visibility between linked and not-linked sections
        if (hasLinked) {
            notLinkedSection?.classList.add('hide');
            linkedSection?.classList.remove('hide');
        } else {
            notLinkedSection?.classList.remove('hide');
            linkedSection?.classList.add('hide');
        }

        // Handle the link button state (only relevant when not linked)
        if (linkBtn && !hasLinked) {
            if (isModified) {
                linkBtn.classList.add('disabled');
                linkBtn.title = 'Please save group changes before linking';
                linkBtn.style.pointerEvents = 'none';
            } else {
                linkBtn.classList.remove('disabled');
                linkBtn.title = '';
                linkBtn.style.pointerEvents = '';
            }
        }

        // If chat type is Channel or Private, enforce SyncUserNames disabled
        const chatType = groupData.TelegramGroupChat?.ChatType;
        const isUnsupportedSync = chatType === 'Channel' || chatType === 'Private' || chatType === 2 || chatType === 3;
        if (sync) {
            sync.disabled = isUnsupportedSync;
            if (isUnsupportedSync) {
                sync.checked = false;
                sync.parentElement.title = 'Username sync is not applicable for Channel or Private chats';
            } else {
                sync.parentElement.title = '';
            }
        }
    },

    unlinkGroup: (page) => {
        if (!tgConfigPage.currentGroup) {
            window.Dashboard.alert('Please select a group first');
            return Promise.resolve();
        }

        if (!confirm(`Are you sure you want to unlink the Telegram chat from group "${tgConfigPage.currentGroup}"?\n\nThis will remove the connection but keep the group settings.`)) {
            return Promise.resolve();
        }

        return window.ApiClient.ajax({
            url: window.ApiClient.getUrl(`/api/TeleJellyConfig/UnlinkGroup/${encodeURIComponent(tgConfigPage.currentGroup)}`),
            type: "POST"
        }).then(() => {
            // Clear the modified groups cache for this group to force reload
            tgConfigPage.modifiedGroups.delete(tgConfigPage.currentGroup);
            // Reload the configuration to reflect the changes
            tgConfigPage.loadConfiguration(page);
            window.Dashboard.alert('Group unlinked successfully');
        }).catch((err) => {
            window.Dashboard.alert('Failed to unlink group: ' + (err.message || 'Unknown error'));
        });
    },

    deleteGroup: (page) => {
        if (!tgConfigPage.currentGroup) {
            window.Dashboard.alert('Please select a group to delete');
            return Promise.resolve();
        }

        if (!confirm(`Are you sure you want to delete the group "${tgConfigPage.currentGroup}"?`)) {
            return Promise.resolve();
        }

        return new Promise((resolve) => {
            ApiClient.getPluginConfiguration(tgConfigPage.pluginUniqueId).then((config) => {
                config.TelegramGroups = config.TelegramGroups?.filter(
                    group => group.GroupName !== tgConfigPage.currentGroup
                ) || [];

                ApiClient.updatePluginConfiguration(
                    tgConfigPage.pluginUniqueId,
                    config
                ).then(function (result) {
                    window.Dashboard.processPluginConfigurationUpdateResult(result);
                    tgConfigPage.currentGroup = null;
                    // remove group & disable inputs
                    tgConfigPage.populateGroups(page, config);
                    // Clear the form
                    page.querySelector("#EnableAllFolders").checked = false;
                    page.querySelector("#UserNames").value = '';
                    page.querySelectorAll('.folder-checkbox').forEach(cb => cb.checked = false);
                    page.querySelector("#LinkedTelegramGroupId").innerHTML = "None";
                    page.querySelector("#BotLinkCommandUrl").href = `https://t.me/${tgTokenHelper.currentUserName}?startgroup`;
                    resolve();
                });
            });
        });
    },

    saveGroupConfig: (page) => {
        // Ensure current group changes are tracked before saving
        tgConfigPage.updateGroupData(page);

        return new Promise((resolve) => {
            ApiClient.getPluginConfiguration(tgConfigPage.pluginUniqueId).then((config) => {
                if (!config.TelegramGroups) {
                    config.TelegramGroups = [];
                }

                // Update all modified groups
                for (let [groupName, groupData] of tgConfigPage.modifiedGroups) {
                    const groupIndex = config.TelegramGroups.findIndex(g => g.GroupName === groupName);
                    if (groupIndex !== -1) {
                        const current = config.TelegramGroups[groupIndex];
                        const updated = {...current, ...groupData};

                        if (groupData.TelegramGroupChat !== undefined) {
                            updated.TelegramGroupChat = {
                                ...(current.TelegramGroupChat || {}),
                                ...groupData.TelegramGroupChat
                            };
                        }

                        config.TelegramGroups[groupIndex] = updated;
                    }
                }

                ApiClient.updatePluginConfiguration(
                    tgConfigPage.pluginUniqueId,
                    config
                ).then(function (result) {
                    window.Dashboard.processPluginConfigurationUpdateResult(result);
                    // Clear modified groups after successful save
                    tgConfigPage.modifiedGroups.clear();
                    resolve();
                });
            });
        });
    },


    /** ======== ======== LIBRARY CONFIG ======== ======== */

    populateFolders: (container) => {

        const folderContainer = container.querySelector("#EnabledFolders");

        return window.ApiClient.getJSON(
            window.ApiClient.getUrl("Library/MediaFolders", {
                IsHidden: false
            })
        ).then((folders) => {
            tgConfigPage.populateFolderElements(folderContainer, folders.Items);
        });
    },

    populateEnabledFolders: (folderList, container) => {
        container.querySelectorAll(".folder-checkbox").forEach((e) => {
            e.checked = folderList.includes(e.getAttribute("data-id"));
        });
    },

    serializeEnabledFolders: (container) => {
        return [...container.querySelectorAll(".folder-checkbox")]
            .filter((e) => e.checked)
            .map((e) => {
                return e.getAttribute("data-id");
            });
    },


    /*
    container: html element
    folders.Items: array of objects, with .Id & .Name
    */
    populateFolderElements: (container, folderItems) => {
        container
            .querySelectorAll(".emby-checkbox-label")
            .forEach((e) => e.remove());

        const checkboxes = folderItems.map((folder) => {
            const out = document.createElement("label");
            out.innerHTML = `
                <input
                    is="emby-checkbox"
                    class="folder-checkbox chkFolder"
                    data-id="${folder.Id}"
                    type="checkbox"
                />
                <span>${folder.Name}</span>
            `;
            return out;
        });

        if (checkboxes.length === 0 && container.children.length === 0) {
            const missing = document.createElement("label");
            missing.innerHTML = "<span>No Media Libraries configured.</span>";
            checkboxes.push(missing);
        }

        checkboxes.forEach((e) => {
            container.appendChild(e);
        });
    },


    /** ======== ======== UTILS ======== ======== */


    parseTextList: (element) => {
        // element is a textarea input element
        // Return the parsed text list
        return element.value
            .split("\n")
            .map((e) => e.trim())
            .filter((e) => e);
    },

    addTextAreaStyle: (view) => {
        const style = document.createElement("link");
        style.rel = "stylesheet";
        style.href = window.ApiClient.getUrl("web/configurationpage") + "?name=TeleJelly.css";
        view.appendChild(style);
    },

    toggleTokenFunction: (e) => {
        const tokenField = document.getElementById("TgBotToken");
        if (tokenField.type === "password") {
            tokenField.type = "text";
        } else {
            tokenField.type = "password";
        }
    },

    startAutoRefresh: (page) => {
        tgConfigPage.stopAutoRefresh(page);
        page.__teleJellyAutoRefresh = window.setInterval(() => {
            tgConfigPage.loadDownloads(page);
            tgConfigPage.loadDownloadLogs(page);
        }, tgConfigPage.autoRefreshIntervalMs);
    },

    stopAutoRefresh: (page) => {
        if (page.__teleJellyAutoRefresh) {
            window.clearInterval(page.__teleJellyAutoRefresh);
            page.__teleJellyAutoRefresh = null;
        }
    }
};


const tgTokenHelper = {

    currentToken: "12341234:xxxxxxxx",
    currentUserName: "INVALID_BOT_TOKEN",

    // Function to call the validation API
    validateToken(page, token) {
        // disable save button
        const saveButton = page.querySelector("#SaveConfig");
        saveButton.disabled = true;
        saveButton.classList.add("raised");

        tgTokenHelper.currentToken = token.trim();
        return window.ApiClient.ajax(
            {
                url: window.ApiClient.getUrl("/api/TeleJellyConfig/ValidateBotToken"),
                type: "POST",
                data: JSON.stringify({Token: token}),
                contentType: "application/json",
                dataType: "json"
            })
            .then(data => {
                tgTokenHelper.handleValidationResponse(page, data);
            })
            .catch(error => {
                tgTokenHelper.handleValidationResponse(page, {ErrorMessage: error.message});
            });
    },

    // Function to handle the API response
    handleValidationResponse(page, data) {
        const tokenElement = page.querySelector("#TgBotToken");
        const nameElement = page.querySelector("#TgBotUsername");
        if (data?.Ok) {
            nameElement.style.color = tokenElement.style.borderColor = "limegreen";
            tgTokenHelper.currentUserName = data.BotUsername;
            nameElement.innerHTML = `@${data.BotUsername}`;

            // update Bot Link-Command Url
            if (tgConfigPage.currentGroup) {
                const encodedText = btoa(`${LinkPrefix}${tgConfigPage.currentGroup}`);
                page.querySelector("#BotLinkCommandUrl").href = `https://t.me/${data.BotUsername}?startgroup=${encodedText}`;
            } else {
                page.querySelector("#BotLinkCommandUrl").href = `https://t.me/${data.BotUsername}?startgroup`;
            }

            // enable save button
            const saveButton = page.querySelector("#SaveConfig");
            saveButton.disabled = false;
            saveButton.classList.remove("raised");
        } else {
            nameElement.style.color = tokenElement.style.borderColor = "indianred";
            tgTokenHelper.currentUserName = "";
            nameElement.innerHTML = data.ErrorMessage || "Invalid token";
        }
    }
}


export default function (view) {
    window.Dashboard.showLoadingMsg();

    tgConfigPage.addTextAreaStyle(view);
    tgConfigPage.loadConfiguration(view);
    tgConfigPage.loadRequests(view);
    tgConfigPage.loadDownloads(view);
    tgConfigPage.loadDownloadLogs(view);

    tgConfigPage.populateFolders(view).then(() => {
        const inputs = [
            "#EnableAllFolders",
            "#UserNames",
            ".folder-checkbox",
            "#SyncUserNames",
            "#NotifyNewContent",
            "#AllowRequests"
        ];

        inputs.forEach(selector => {
            const elements = view.querySelectorAll(selector);
            elements.forEach(element => {
                element.addEventListener('change', () => tgConfigPage.updateGroupData(view));
            });
        });
    });

    view.querySelector("#show-hide-token").addEventListener("click", (e) => {
        e.preventDefault();
        tgConfigPage.toggleTokenFunction(e);
    });

    // Trim LoginBaseUrl on change and update login URL
    view.querySelector("#LoginBaseUrl").addEventListener("change", (e) => {
        const input = view.querySelector('#LoginBaseUrl');
        let inputValue = input?.value?.trim() || '';
        if (inputValue.endsWith("/")) {
            inputValue = inputValue.substring(0, inputValue.length - 1);
            input.value = inputValue;
        }
        // Update the login URL display in real-time
        tgConfigPage.updateLoginUrl(view, inputValue);
    });

    // Basic configuration event
    view.querySelector("#SaveConfig").addEventListener("click", async (e) => {
        e.preventDefault();
        await tgConfigPage.saveConfig(view);
    });

    view.querySelector("#FormatDownloadManagerConfig").addEventListener("click", (e) => {
        e.preventDefault();
        tgConfigPage.formatDownloadManagerConfig(view);
    });

    view.querySelector("#ReloadDownloadManagerConfig").addEventListener("click", (e) => {
        e.preventDefault();
        tgConfigPage.reloadDownloadManagerConfig(view);
    });

    // Group management events
    view.querySelector("#EnableAllFolders").addEventListener("change", (e) => {
        const checkboxes = view.querySelectorAll('.folder-checkbox');
        checkboxes.forEach(cb => {
            cb.disabled = e.target.checked;
            if (e.target.checked) {
                cb.checked = true;
            }
        });
        tgConfigPage.updateGroupData(view);
        tgConfigPage.updateGroupEditingState(view);
    });

    view.querySelector("#AddGroup").addEventListener("click", (e) => {
        e.preventDefault();
        tgConfigPage.addGroup(view);
    });

    view.querySelector("#SaveGroupConfig").addEventListener("click", (e) => {
        e.preventDefault();
        tgConfigPage.saveGroupConfig(view);
    });

    view.querySelector("#DeleteGroup").addEventListener("click", (e) => {
        e.preventDefault();
        tgConfigPage.deleteGroup(view);
    });

    view.querySelector("#UnlinkGroup").addEventListener("click", (e) => {
        e.preventDefault();
        tgConfigPage.unlinkGroup(view);
    });

    // Request events
    view.querySelector("#RefreshRequests").addEventListener("click", (e) => {
        e.preventDefault();
        tgConfigPage.loadRequests(view);
        window.Dashboard.alert('Request list refreshed');
    });

    view.querySelector("#AddManualRequest").addEventListener("click", (e) => {
        e.preventDefault();
        tgConfigPage.addRequest(view);
    });

    // Download manager events
    view.querySelector("#RefreshDownloads").addEventListener("click", (e) => {
        e.preventDefault();
        tgConfigPage.loadDownloads(view);
        window.Dashboard.alert('Download list refreshed');
    });
    view.querySelector("#DownloadStatusFilter").addEventListener("change", () => {
        tgConfigPage.loadDownloads(view);
    });
    view.querySelector("#RefreshDownloadLogs").addEventListener("click", (e) => {
        e.preventDefault();
        tgConfigPage.loadDownloadLogs(view);
        window.Dashboard.alert('Download manager log refreshed');
    });

    // Bot token validation
    let debounce;
    const inputElement = view.querySelector("#TgBotToken");
    inputElement.addEventListener("input", () => {
        clearTimeout(debounce);
        debounce = setTimeout(() => tgTokenHelper.validateToken(view, inputElement.value), 250);
    });

    // Note: Login URL and branding widget are now set dynamically in populateConfiguration
    // based on LoginBaseUrl value via updateLoginUrl()

    tgConfigPage.startAutoRefresh(view);
    const cleanupAutoRefresh = () => tgConfigPage.stopAutoRefresh(view);
    view.addEventListener("viewhide", cleanupAutoRefresh, {once: true});
    view.addEventListener("pagehide", cleanupAutoRefresh, {once: true});

    window.Dashboard.hideLoadingMsg();
}

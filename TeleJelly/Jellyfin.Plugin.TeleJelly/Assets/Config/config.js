const tgConfigPage = {
    pluginUniqueId: "4b71013d-00ba-470c-9e4d-0c451a435328",

    loadConfiguration: (page) => {
        ApiClient.getPluginConfiguration(tgConfigPage.pluginUniqueId).then(
            (config) => {
                tgConfigPage.populateConfiguration(page, config);
            }
        );

        const folderContainer = page.querySelector("#EnabledFolders");
        tgConfigPage.populateFolders(folderContainer);
    },

    populateConfiguration: (page, config) => {
        if (config.BotToken) {
            tgTokenHelper.validateToken(config.BotToken);
        }

        // Set basic config values
        page.querySelector("#TgBotToken").value = config.BotToken || tgTokenHelper.currentToken;
        page.querySelector("#TgBotUsername").innerHTML = config.BotUsername || tgTokenHelper.currentUserName;
        page.querySelector("#TgAdministrators").value = config.AdminUserNames?.join("\r\n") || "";
        page.querySelector("#ForcedUrlScheme").value = config.ForcedUrlScheme || "none";

        // Populate group list
        const groupList = page.querySelector("#groupList");
        groupList.innerHTML = ''; // Clear existing groups

        config.TelegramGroups?.forEach((group) => {
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
        }
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
    populateFolders: (container) => {
        return window.ApiClient.getJSON(
            window.ApiClient.getUrl("Library/MediaFolders", {
                IsHidden: false
            })
        ).then((folders) => {
            tgConfigPage.populateFolderElements(container, folders);
        });
    },
    /*
    container: html element
    folders.Items: array of objects, with .Id & .Name
    */
    populateFolderElements: (container, folders) => {
        container
            .querySelectorAll(".emby-checkbox-label")
            .forEach((e) => e.remove());

        const checkboxes = folders.Items.map((folder) => {
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

    saveConfig: (page) => {
        return new Promise((resolve) => {
            window.ApiClient.getPluginConfiguration(
                tgConfigPage.pluginUniqueId
            ).then((config) => {
                // apply basic config
                config.BotToken = tgTokenHelper.currentToken;
                config.BotUsername = tgTokenHelper.currentUserName;
                config.AdminUserNames = tgConfigPage.parseTextList(page.querySelector("#TgAdministrators"));
                config.ForcedUrlScheme = page.querySelector("#ForcedUrlScheme").value || "none";

                // save it
                window.ApiClient.updatePluginConfiguration(
                    tgConfigPage.pluginUniqueId,
                    config
                ).then(function (result) {
                    window.Dashboard.processPluginConfigurationUpdateResult(result);
                    tgConfigPage.loadConfiguration(page);
                    resolve();
                });
            });
        });
    },

    selectGroup: (page, groupName) => {
        tgConfigPage.currentGroup = groupName;

        // Update selected state
        page.querySelectorAll('.group-item').forEach(item => {
            item.classList.toggle('selected', item.getAttribute('data-group-name') === groupName);
        });

        // Load group data
        ApiClient.getPluginConfiguration(tgConfigPage.pluginUniqueId).then((config) => {
            const groupData = config.TelegramGroups?.find(group => group.GroupName === groupName);
            if (groupData) {
                page.querySelector("#EnableAllFolders").checked = groupData.EnableAllFolders;
                tgConfigPage.populateEnabledFolders(groupData.EnabledFolders || [], page.querySelector("#EnabledFolders"));
                page.querySelector("#LinkedTelegramGroupId").innerHTML = groupData.LinkedTelegramGroupId ?? "None";
                page.querySelector("#UserNames").value = groupData.UserNames.join("\r\n");
            }
        });
    },


    addGroup: (page) => {
        const newGroupName = page.querySelector("#TgGroupName").value.trim();
        if (!newGroupName) {
            window.Dashboard.alert('Please enter a group name');
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
                tgConfigPage.loadConfiguration(page);
                tgConfigPage.selectGroup(page, newGroupName);
                page.querySelector("#TgGroupName").value = ''; // Clear input after adding
            });
        });
    },

    saveGroupConfig: (page) => {
        if (!tgConfigPage.currentGroup) {
            window.Dashboard.alert('Please select a group to save changes');
            return Promise.resolve();
        }

        return new Promise((resolve) => {
            ApiClient.getPluginConfiguration(tgConfigPage.pluginUniqueId).then((config) => {
                const groupIndex = config.TelegramGroups?.findIndex(g => g.GroupName === tgConfigPage.currentGroup);
                if (groupIndex === -1) {
                    window.Dashboard.alert('Selected group not found');
                    return;
                }

                // Update group data
                config.TelegramGroups[groupIndex] = {
                    ...config.TelegramGroups[groupIndex],
                    GroupName: tgConfigPage.currentGroup,
                    EnableAllFolders: page.querySelector("#EnableAllFolders").checked,
                    EnabledFolders: tgConfigPage.serializeEnabledFolders(page),
                    UserNames: tgConfigPage.parseTextList(page.querySelector("#UserNames"))
                };

                ApiClient.updatePluginConfiguration(
                    tgConfigPage.pluginUniqueId,
                    config
                ).then(function (result) {
                    window.Dashboard.processPluginConfigurationUpdateResult(result);
                    window.Dashboard.alert('Group settings saved successfully');
                    resolve();
                });
            });
        });
    },

    parseTextList: (element) => {
        // element is a textarea input element
        // Return the parsed text list
        return element.value
            .split("\n")
            .map((e) => e.trim())
            .filter((e) => e);
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
                    tgConfigPage.loadConfiguration(page);
                    // Clear the form
                    page.querySelector("#EnableAllFolders").checked = false;
                    page.querySelector("#UserNames").value = '';
                    page.querySelectorAll('.folder-checkbox').forEach(cb => cb.checked = false);
                    page.querySelector("#LinkedTelegramGroupId").innerHTML = "None";
                    resolve();
                });
            });
        });
    },

    addTextAreaStyle: (view) => {
        const style = document.createElement("link");
        style.rel = "stylesheet";
        style.href = window.ApiClient.getUrl("web/configurationpage") + "?name=TeleJelly.css";
        view.appendChild(style);
    }
};


const tgTokenHelper = {

    currentToken: "12341234:xxxxxxxx",
    currentUserName: "ExampleBot",

    // Function to call the validation API
    validateToken(token) {
        // disable save button
        const saveButton = document.getElementById("SaveConfig");
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
                tgTokenHelper.handleValidationResponse(data);
            })
            .catch(error => {
                tgTokenHelper.handleValidationResponse({ErrorMessage: error.message});
            });
    },

    // Function to handle the API response
    handleValidationResponse(data) {
        const tokenElement = document.getElementById("TgBotToken");
        const nameElement = document.getElementById("TgBotUsername");
        if (data.Ok) {
            nameElement.style.color = tokenElement.style.borderColor = "limegreen";
            tgTokenHelper.currentUserName = data.BotUsername;
            nameElement.innerHTML = `@${data.BotUsername}`;
            // enable save button
            const saveButton = document.getElementById("SaveConfig");
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

    // Basic configuration event
    view.querySelector("#SaveConfig").addEventListener("click", async (e) => {
        e.preventDefault();
        await tgConfigPage.saveConfig(view);
    });

    // Group management events
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

    // Bot token validation
    let debounce;
    const inputElement = view.querySelector("#TgBotToken");
    inputElement.addEventListener("input", function () {
        clearTimeout(debounce);
        debounce = setTimeout(() => tgTokenHelper.validateToken(inputElement.value), 250);
    });

    window.Dashboard.hideLoadingMsg();
}

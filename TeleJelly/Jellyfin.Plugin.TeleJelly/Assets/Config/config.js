const tgConfigPage = {
    pluginUniqueId: "4b71013d-00ba-470c-9e4d-0c451a435328",

    // Track modified groups separately from loaded config
    modifiedGroups: new Map(),
    currentGroup: null,


    /** ======== ======== GENERAL CONFIG ======== ======== */


    loadConfiguration: (page) => {
        ApiClient.getPluginConfiguration(tgConfigPage.pluginUniqueId).then(
            (config) => {
                tgConfigPage.populateConfiguration(page, config);
                tgConfigPage.populateGroups(page, config);
            }
        );
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
    },


    populateGroups: (page, config) => {
        // Populate group list
        const groupList = page.querySelector("#groupList");
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


    /** ======== ======== GROUP CONFIG ======== ======== */


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
                tgConfigPage.currentGroup = newGroupName;
                tgConfigPage.populateGroups(page, config);
                page.querySelector("#TgGroupName").value = ''; // Clear input after adding
            });
        });
    },

    updateGroupEditingState: (page) => {
        const hasSelectedGroup = !!tgConfigPage.currentGroup;

        // Elements to toggle
        const userNamesList = page.querySelector("#UserNames");
        const enableAllFolders = page.querySelector("#EnableAllFolders");
        const folderList = page.querySelector("#EnabledFolders");
        const delGroupBtn = page.querySelector("#DeleteGroup");
        const folderCheckboxes = page.querySelectorAll('.folder-checkbox');

        // Disable/enable elements
        [userNamesList, enableAllFolders, delGroupBtn, ...folderCheckboxes].forEach(element => {
            if (element) {
                element.disabled = !hasSelectedGroup;
                element.title = hasSelectedGroup ? "" : "Please select or create a group first";
            }
        });

        // Handle folder list checkboxes
        if (folderList) {
            const checkboxes = folderList.querySelectorAll('input[type="checkbox"]');
            checkboxes.forEach(checkbox => {
                checkbox.disabled = !hasSelectedGroup;
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

        const groupData = {
            GroupName: tgConfigPage.currentGroup,
            EnableAllFolders: page.querySelector("#EnableAllFolders").checked,
            EnabledFolders: tgConfigPage.serializeEnabledFolders(page),
            UserNames: tgConfigPage.parseTextList(page.querySelector("#UserNames")),
        };

        tgConfigPage.modifiedGroups.set(tgConfigPage.currentGroup, groupData);
    },

    selectGroup: (page, groupName) => {
        tgConfigPage.currentGroup = groupName;

        console.debug(`Selecting group: ${groupName}.`);

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

            page.querySelector("#LinkedTelegramGroupId").innerHTML = groupData.LinkedTelegramGroupId ?? "None";
            page.querySelector("#UserNames").value = groupData.UserNames.join("\r\n");
        }
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
                        // keep non-overridden values.
                        config.TelegramGroups[groupIndex] = {
                            ...config.TelegramGroups[groupIndex],
                            ...groupData
                        };
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
            tgConfigPage.populateFolderElements(folderContainer, folders);
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
        const x = document.getElementById("TgBotToken");
        if (x.type === "password") {
            x.type = "text";
        } else {
            x.type = "password";
        }
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
    tgConfigPage.populateFolders(view).then(() => {
        const inputs = [
            "#EnableAllFolders",
            "#UserNames",
            ".folder-checkbox"
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

    // Basic configuration event
    view.querySelector("#SaveConfig").addEventListener("click", async (e) => {
        e.preventDefault();
        await tgConfigPage.saveConfig(view);
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

    // Bot token validation
    let debounce;
    const inputElement = view.querySelector("#TgBotToken");
    inputElement.addEventListener("input", function () {
        clearTimeout(debounce);
        debounce = setTimeout(() => tgTokenHelper.validateToken(inputElement.value), 250);
    });

    window.Dashboard.hideLoadingMsg();
}

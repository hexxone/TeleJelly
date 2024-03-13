const tgConfigPage = {
    pluginUniqueId: "4b71013d-00ba-470c-9e4d-0c451a435328",
    loadConfiguration: (page) => {
        ApiClient.getPluginConfiguration(tgConfigPage.pluginUniqueId).then(
            (config) => {
                tgConfigPage.populateConfiguration(page, config);
            }
        );

        const folder_container = page.querySelector("#EnabledFolders");
        tgConfigPage.populateFolders(folder_container);
    },
    populateConfiguration: (page, config) => {

        if (config.BotToken) {
            // Validate the token once initially
            tgTokenHelper.validateToken(config.BotToken);
        }

        // set basic config values
        page.querySelector('#TgBotToken').value = config.BotToken || tgTokenHelper.currentToken;
        page.querySelector('#TgBotUsername').innerHTML = config.BotUsername || tgTokenHelper.currentUserName;
        page.querySelector('#TgAdministrators').value = config.AdminUserNames?.join('\r\n') || "";

        page.querySelector('#ForceUrlScheme').checked = config.ForceUrlScheme || false;
        page.querySelector("#ForcedUrlSchemeHolder").style.display = config.ForceUrlScheme ? "block" : "none";
        page.querySelector('#ForcedUrlScheme').value = config.ForcedUrlScheme || "https";

        // Add groups selection Options
        page.querySelectorAll("#selectGroup option").forEach((e) => e.remove());
        config.TelegramGroups?.forEach((group) => {
            page.querySelector("#selectGroup").appendChild(new Option(group.GroupName, group.GroupName));
        });
    },
    populateEnabledFolders: (folder_list, container) => {
        container.querySelectorAll(".folder-checkbox").forEach((e) => {
            e.checked = folder_list.includes(e.getAttribute("data-id"));
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
        return ApiClient.getJSON(
            ApiClient.getUrl("Library/MediaFolders", {
                IsHidden: false,
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
            checkboxes.push(missing)
        }

        checkboxes.forEach((e) => {
            container.appendChild(e);
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
    loadGroup: (page, targetGroup) => {
        ApiClient.getPluginConfiguration(tgConfigPage.pluginUniqueId).then(
            (config) => {
                // get group data to show
                const groupData = config.TelegramGroups?.find((group) => {
                    return group.GroupName === targetGroup;
                }) || console.error("Group not found: " + targetGroup);

                page.querySelector("#TgGroupName").value = targetGroup;
                page.querySelector("#EnableAllFolders").checked = groupData.EnableAllFolders;
                tgConfigPage.populateEnabledFolders(groupData.EnabledFolders || [], page.querySelector("#EnabledFolders"))
                page.querySelector("#UserNames").value = groupData.UserNames.join('\r\n');

                Dashboard.alert("Group Settings loaded.");
            }
        );
    },
    deleteGroup: (page, targetGroup) => {
        if (targetGroup?.trim() === "" ||
            !window.confirm(
                `Are you sure you want to delete the Group ${targetGroup}?`
            )
        ) {
            return;
        }
        return new Promise((resolve) => {
            ApiClient.getPluginConfiguration(
                tgConfigPage.pluginUniqueId
            ).then((config) => {

                config.TelegramGroups = config.TelegramGroups?.filter((group) => {
                    return group.GroupName !== targetGroup;
                }) || [];

                ApiClient.updatePluginConfiguration(
                    tgConfigPage.pluginUniqueId,
                    config
                ).then(function (result) {
                    Dashboard.processPluginConfigurationUpdateResult(result);
                    tgConfigPage.loadConfiguration(page);

                    resolve();
                });
            });
        });
    },
    saveConfig: (page) => {
        return new Promise((resolve) => {
            ApiClient.getPluginConfiguration(
                tgConfigPage.pluginUniqueId
            ).then((config) => {
                // apply config
                config.BotToken = tgTokenHelper.currentToken;
                config.BotUsername = tgTokenHelper.currentUserName;
                config.AdminUserNames = tgConfigPage.parseTextList(page.querySelector('#TgAdministrators'));
                config.ForceUrlScheme = page.querySelector('#ForceUrlScheme').checked || false;
                config.ForcedUrlScheme = page.querySelector('#ForcedUrlScheme').value || "";

                // save it
                ApiClient.updatePluginConfiguration(
                    tgConfigPage.pluginUniqueId,
                    config
                ).then(function (result) {
                    Dashboard.processPluginConfigurationUpdateResult(result);
                    tgConfigPage.loadConfiguration(page);
                    resolve();
                });
            });
        });
    },
    saveGroup: (page, targetGroup) => {
        return new Promise((resolve) => {
            ApiClient.getPluginConfiguration(
                tgConfigPage.pluginUniqueId
            ).then((config) => {
                // create groups if null
                if (!config.TelegramGroups) {
                    config.TelegramGroups = []
                }
                // get old data to overwrite
                const groupData = config.TelegramGroups.find((group) => {
                    return group.GroupName === targetGroup;
                }) || {};
                // remove old data
                config.TelegramGroups = config.TelegramGroups.filter((group) => {
                    return group.GroupName !== targetGroup;
                }) || [];

                groupData.GroupName = page.querySelector("#TgGroupName").value.trim();
                groupData.EnableAllFolders = page.querySelector("#EnableAllFolders").checked === true;
                groupData.EnabledFolders = tgConfigPage.serializeEnabledFolders(page);
                groupData.UserNames = tgConfigPage.parseTextList(page.querySelector("#UserNames"));
                // (re-) add data
                config.TelegramGroups.push(groupData);

                // save it
                ApiClient.updatePluginConfiguration(
                    tgConfigPage.pluginUniqueId,
                    config
                ).then(function (result) {
                    Dashboard.processPluginConfigurationUpdateResult(result);
                    tgConfigPage.loadConfiguration(page);
                    tgConfigPage.loadGroup(page, targetGroup);

                    page.querySelector("#selectGroup").value = targetGroup;
                    resolve();
                });
            });
        });
    },
    addTextAreaStyle: (view) => {
        const style = document.createElement("link");
        style.rel = "stylesheet";
        style.href = ApiClient.getUrl("web/configurationpage") + "?name=TeleJelly.css";
        view.appendChild(style);
    },
};


const tgTokenHelper = {

    currentToken: "12341234:xxxxxxxx",
    currentUserName: "ExampleBot",

    // Function to call the validation API
    validateToken(token) {
        // TODO disable save button
        const saveButton = document.getElementById("SaveConfig");
        saveButton.disabled = true;
        saveButton.classList.add("raised");

        tgTokenHelper.currentToken = token.trim();
        return ApiClient.ajax({
            url: ApiClient.getUrl('/api/TeleJellyConfig/ValidateBotToken'),
            type: 'POST',
            data: JSON.stringify({Token: token}),
            contentType: "application/json",
            dataType: "json"
        })
            .then((data) => {
                tgTokenHelper.handleValidationResponse(data);
            })
            .catch(response => {
                tgTokenHelper.handleValidationResponse({});
            });
    },

    // Function to handle the API response
    handleValidationResponse(data) {
        const tokenElement = document.getElementById('TgBotToken');
        const nameElement = document.getElementById('TgBotUsername');
        if (data.Ok) {
            nameElement.style.color = tokenElement.style.borderColor = 'limegreen';
            tgTokenHelper.currentUserName = data.BotUsername;
            nameElement.innerHTML = `@${data.BotUsername}`;
            // TODO enable save button
            const saveButton = document.getElementById("SaveConfig");
            saveButton.disabled = false;
            saveButton.classList.remove("raised");
        } else {
            nameElement.style.color = tokenElement.style.borderColor = 'indianred';
            tgTokenHelper.currentUserName = '';
            nameElement.innerHTML = data.ErrorMessage || 'Invalid token';
        }
    }
}

export default function (view) {
    Dashboard.showLoadingMsg();

    tgConfigPage.addTextAreaStyle(view);
    tgConfigPage.loadConfiguration(view);

    view.querySelector("#SaveConfig").addEventListener("click", (e) => {
        tgConfigPage.saveConfig(view);
        e.preventDefault();
        return false;
    });

    view.querySelector("#LoadGroup").addEventListener("click", (e) => {
        const targetGroup = view.querySelector("#selectGroup").value;
        tgConfigPage.loadGroup(view, targetGroup);
        e.preventDefault();
        return false;
    });

    view.querySelector("#DeleteGroup").addEventListener("click", (e) => {
        const targetGroup = view.querySelector("#selectGroup").value;
        tgConfigPage.deleteGroup(view, targetGroup);
        e.preventDefault();
        return false;
    });

    view.querySelector("#SaveGroup").addEventListener("click", (e) => {
        const targetGroup = view.querySelector("#TgGroupName").value;
        tgConfigPage.saveGroup(view, targetGroup);
        e.preventDefault();
        return false;
    });

    const loginUrl = ApiClient.getUrl("/sso/Telegram/Login");
    view.querySelector("#sso-telegram-login").href = loginUrl;
    view.querySelector("#exampleBrandingCode").innerHTML = `[Telegram-Login](${loginUrl})`;

    // Event listener for input changes with debounce
    let debounce;
    const inputElement = view.querySelector("#TgBotToken");
    inputElement.addEventListener('input', function () {
        clearTimeout(debounce);
        debounce = setTimeout(() => tgTokenHelper.validateToken(inputElement.value), 250);
    });

    Dashboard.hideLoadingMsg();
}

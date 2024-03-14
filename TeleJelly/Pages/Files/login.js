const loadingSpinner = {
    // depends on https://github.com/jellyfin/jellyfin-web/blob/master/src/components/loading/loading.scss

    loader: undefined,

    createLoader: () => {
        const elem = document.createElement('div');
        elem.setAttribute('dir', 'ltr');
        elem.classList.add('docspinner');
        elem.classList.add('mdl-spinner');

        elem.innerHTML = '<div class="mdl-spinner__layer mdl-spinner__layer-1"><div class="mdl-spinner__circle-clipper mdl-spinner__left"><div class="mdl-spinner__circle mdl-spinner__circleLeft"></div></div><div class="mdl-spinner__circle-clipper mdl-spinner__right"><div class="mdl-spinner__circle mdl-spinner__circleRight"></div></div></div><div class="mdl-spinner__layer mdl-spinner__layer-2"><div class="mdl-spinner__circle-clipper mdl-spinner__left"><div class="mdl-spinner__circle mdl-spinner__circleLeft"></div></div><div class="mdl-spinner__circle-clipper mdl-spinner__right"><div class="mdl-spinner__circle mdl-spinner__circleRight"></div></div></div><div class="mdl-spinner__layer mdl-spinner__layer-3"><div class="mdl-spinner__circle-clipper mdl-spinner__left"><div class="mdl-spinner__circle mdl-spinner__circleLeft"></div></div><div class="mdl-spinner__circle-clipper mdl-spinner__right"><div class="mdl-spinner__circle mdl-spinner__circleRight"></div></div></div><div class="mdl-spinner__layer mdl-spinner__layer-4"><div class="mdl-spinner__circle-clipper mdl-spinner__left"><div class="mdl-spinner__circle mdl-spinner__circleLeft"></div></div><div class="mdl-spinner__circle-clipper mdl-spinner__right"><div class="mdl-spinner__circle mdl-spinner__circleRight"></div></div></div>';

        document.body.appendChild(elem);
        return elem;
    },

    show: () => {
        if (!loadingSpinner.loader) {
            loadingSpinner.loader = loadingSpinner.createLoader();
        }
        loadingSpinner.loader.classList.add('mdlSpinnerActive');
    },

    hide: () => {
        if (loadingSpinner.loader) {
            loadingSpinner.loader.classList.remove('mdlSpinnerActive');
        }
    }
};


// get data from login widget
function onTelegramAuth(user) {
    loadingSpinner.show();
    console.debug("Logging in as User", user);
    teleJellyAuthenticate(user);
}

// send data to API Controller
function teleJellyAuthenticate(user) {
    fetch("{{SERVER_URL}}/sso/telegram/authenticate", {
        method: "POST",
        body: JSON.stringify(user),
        headers: {
            "Content-type": "application/json; charset=UTF-8"
        }
    }).then((response) => response.json())
        .then((json) => teleJellyResponse(json));
}

// receive JSON response, redirect or show error.
function teleJellyResponse(data) {
    if (data.Ok) {
        setCredentialsAndRedirect(data.AuthenticatedUser);
    } else {
        showError(data.ErrorMessage ?? "Unknown Error");
        loadingSpinner.hide();
    }
}

function showError(message) {
    const elem = document.getElementById("errorMessage");
    elem.textContent = message;
    elem.style.padding = "1rem";
    elem.style.border = "1px dotted #dd4444";
}

function setCredentialsAndRedirect(resultData) {
    if (resultData === undefined) {
        console.warn(
            "Error parsing Result Data: ",
            resultDataString
        );
        return;
    }

    resultData.User.Id = resultData.User.Id.replaceAll("-", "");

    // ======== remove null data ========

    const userKeys = Object.keys(resultData.User);
    userKeys.forEach((element) => {
        if (
            resultData.User[element] === null ||
            resultData.User[element] === undefined
        ) {
            delete resultData.User[element];
        }
    });

    //========  user-userId-serverId ========

    const userId = `user-${resultData.User.Id}-${resultData.User.ServerId}`;
    localStorage.setItem(userId, JSON.stringify(resultData.User));

    // ======== jellyfin_credentials ========

    const storedCreds = JSON.parse(
        localStorage.getItem("jellyfin_credentials") || "{}"
    );
    storedCreds.Servers = storedCreds.Servers || [];
    // apply single
    const currentServer = storedCreds.Servers[0] || {};
    currentServer.UserId = resultData.User.Id;
    currentServer.Id = resultData.User.ServerId;
    currentServer.AccessToken = resultData.AccessToken;
    currentServer.ManualAddress = "{{SERVER_URL}}";
    currentServer.manualAddressOnly = true;
    // save if new
    storedCreds.Servers[0] = currentServer;

    localStorage.setItem(
        "jellyfin_credentials",
        JSON.stringify(storedCreds)
    );
    localStorage.setItem("enableAutoLogin", "true");

    // ======== _deviceId2 ========

    var deviceId = localStorage.getItem("_deviceId2");
    if (!deviceId) {
        deviceId = generateDeviceId2();
        localStorage.setItem("_deviceId2", deviceId);
    }

    setTimeout(() => {
        window.location.replace("{{SERVER_URL}}");
    }, 200);
}

function generateDeviceId2() {
    return btoa(
        [navigator.userAgent, new Date().getTime()].join("|")
    ).replace(/=/g, "1");
}

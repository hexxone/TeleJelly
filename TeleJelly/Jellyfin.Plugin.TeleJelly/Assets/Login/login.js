// animates rotation on loading
const loadingSpinner = {
    loader: undefined,

    show: () => {
        if (!loadingSpinner.loader) {
            loadingSpinner.loader = document.getElementById("ssoSyncIcon");
        }
        if (loadingSpinner.loader) {
            loadingSpinner.loader.classList.add('animate-continuous');
        }
    },

    hide: () => {
        if (loadingSpinner.loader) {
            loadingSpinner.loader.classList.remove('animate-continuous');
        }
    }
};


// get data from login widget
function onTelegramAuth(user) {
    loadingSpinner.show();
    teleJellyAuthenticate(user);
}

let deviceId;
let deviceName;

// send data to API Controller
function teleJellyAuthenticate(user) {

    // ======== _deviceId2 && deviceName ========

    if (!deviceName) {
        deviceName = getDeviceName();
    }
    if (!deviceId) {
        deviceId = localStorage.getItem("_deviceId2");
        if (!deviceId) {
            deviceId = generateDeviceId2();
            localStorage.setItem("_deviceId2", deviceId);
        }
    }

    fetch("{{SERVER_URL}}/sso/telegram/authenticate", {
        method: "POST",
        body: JSON.stringify(user),
        headers: {
            "Content-type": "application/json; charset=UTF-8",
            "X-DeviceName": deviceName,
            "X-DeviceId": deviceId
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
    if (!resultData) {
        console.warn(
            "Error parsing Result Data: ",
            resultData
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

    setTimeout(() => {
        window.location.replace("{{SERVER_URL}}");
    }, 200);
}

function generateDeviceId2() {
    return btoa(
        [navigator.userAgent, new Date().toISOString()].join("|")
    ).replace(/=/g, "1");
}

// Simplified code from: https://github.com/jellyfin/jellyfin-web/blob/master/src/scripts/browser.js
function detectBrowser() {
    const userAgent = navigator.userAgent.toLowerCase();
    const browser = {};

    // Basic platform detection
    browser.ipad = /ipad/.test(userAgent);
    browser.iphone = /iphone/.test(userAgent);
    browser.android = /android/.test(userAgent);

    // TV platforms
    browser.tizen = userAgent.includes('tizen') || window.tizen != null;
    browser.web0s = userAgent.includes('netcast') || userAgent.includes('web0s');
    browser.operaTv = userAgent.includes('tv') && userAgent.includes('opr/');
    browser.xboxOne = userAgent.includes('xbox');
    browser.ps4 = userAgent.includes('playstation 4');

    // Desktop browsers
    const edgeRegex = /(edg|edge|edga|edgios)[ /]([\w.]+)/.test(userAgent);
    browser.edgeChromium = /(edg|edga|edgios)[ /]([\w.]+)/.test(userAgent);
    browser.edge = edgeRegex && !browser.edgeChromium;
    browser.chrome = /chrome/.test(userAgent) && !edgeRegex;
    browser.firefox = /firefox/.test(userAgent);
    browser.opera = /opera/.test(userAgent) || /opr/.test(userAgent);
    browser.safari = !browser.chrome && !browser.edgeChromium &&
        !browser.edge && !browser.opera &&
        userAgent.includes('webkit');

    // iPad on iOS 13+ detection
    if (!browser.ipad && navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1) {
        browser.ipad = true;
    }

    return browser;
};

const BrowserName = {
    tizen: 'Samsung Smart TV',
    web0s: 'LG Smart TV',
    operaTv: 'Opera TV',
    xboxOne: 'Xbox One',
    ps4: 'Sony PS4',
    chrome: 'Chrome',
    edgeChromium: 'Edge Chromium',
    edge: 'Edge',
    firefox: 'Firefox',
    opera: 'Opera',
    safari: 'Safari'
};

function getDeviceName() {
    const browser = detectBrowser();
    var name = 'Web Browser - Telegram SSO'; // Default device name

    for (const key in BrowserName) {
        if (browser[key]) {
            name = BrowserName[key];
            break;
        }
    }

    if (browser.ipad) {
        name += ' iPad';
    } else if (browser.iphone) {
        name += ' iPhone';
    } else if (browser.android) {
        name += ' Android';
    }

    return name;
}

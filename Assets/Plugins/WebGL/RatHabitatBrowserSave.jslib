mergeInto(LibraryManager.library, {
    RatHabitatBrowserRead: function (keyPtr) {
        var key = UTF8ToString(keyPtr);
        try {
            var value = window.localStorage.getItem(key);
            return allocateUTF8(value === null ? "" : value);
        } catch (error) {
            console.warn("Rat Habitat browser save read failed", error);
            return allocateUTF8("");
        }
    },

    RatHabitatBrowserWrite: function (keyPtr, valuePtr) {
        var key = UTF8ToString(keyPtr);
        var value = UTF8ToString(valuePtr);
        try {
            window.localStorage.setItem(key, value);
            return 1;
        } catch (error) {
            console.error("Rat Habitat browser save write failed", error);
            return 0;
        }
    },

    RatHabitatBrowserRemove: function (keyPtr) {
        var key = UTF8ToString(keyPtr);
        try {
            window.localStorage.removeItem(key);
        } catch (error) {
            console.warn("Rat Habitat browser save remove failed", error);
        }
    },

    RatHabitatBrowserFlush: function (keyPtr) {
        var key = UTF8ToString(keyPtr);
        try {
            // localStorage is synchronous. Re-setting the last serialized
            // payload makes the requested flush explicit for pagehide and
            // browser unload handlers without requiring sessionStorage.
            var value = window.localStorage.getItem(key);
            if (value !== null) window.localStorage.setItem(key, value);
        } catch (error) {
            console.warn("Rat Habitat browser save flush failed", error);
        }
    },

    RatHabitatBrowserRegisterLifecycle: function (keyPtr) {
        var key = UTF8ToString(keyPtr);
        try {
            if (!window.__ratHabitatSaveLifecycleKeys) window.__ratHabitatSaveLifecycleKeys = {};
            if (window.__ratHabitatSaveLifecycleKeys[key]) return;
            window.__ratHabitatSaveLifecycleKeys[key] = true;

            var flush = function () {
                try {
                    var value = window.localStorage.getItem(key);
                    if (value !== null) window.localStorage.setItem(key, value);
                } catch (error) {
                    // The normal save path already reports write failures;
                    // unload handlers must never block page navigation.
                }
            };

            window.addEventListener("pagehide", flush, false);
            window.addEventListener("beforeunload", flush, false);
            document.addEventListener("visibilitychange", function () {
                if (document.visibilityState === "hidden") flush();
            }, false);
        } catch (error) {
            console.warn("Rat Habitat browser save lifecycle registration failed", error);
        }
    }
});

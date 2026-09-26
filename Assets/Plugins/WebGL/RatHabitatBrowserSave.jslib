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
    },

    // Screen Wake Lock is deliberately kept outside the Unity main loop. The
    // request returns a Promise and the browser can revoke the sentinel when
    // the tab is hidden, the battery is low, or power-saving mode is active.
    // Unity polls only the compact integer status below.
    RatHabitatBrowserWakeLockSetDesired: function (enabled) {
        try {
            var state = window.__ratHabitatWakeLockState;
            if (!state) {
                state = window.__ratHabitatWakeLockState = {
                    enabled: false,
                    sentinel: null,
                    pending: false,
                    status: 4,
                    listenersInstalled: false
                };
            }
            state.enabled = !!enabled;
            if (!state.enabled) {
                state.pending = false;
                state.status = 4; // disabled
                if (state.sentinel) {
                    var sentinel = state.sentinel;
                    state.sentinel = null;
                    try { sentinel.release(); } catch (releaseError) { }
                }
                return;
            }
            if (!navigator.wakeLock || typeof navigator.wakeLock.request !== "function") {
                state.status = 2; // unsupported
            } else if (document.visibilityState !== "visible") {
                state.status = 5; // waiting for a visible page
            } else if (!state.sentinel && !state.pending) {
                state.status = 5; // awaiting a user-gesture request
            }
        } catch (error) {
            // A browser integration failure must never affect the game loop.
        }
    },

    RatHabitatBrowserWakeLockRequest: function () {
        try {
            var state = window.__ratHabitatWakeLockState;
            if (!state) {
                state = window.__ratHabitatWakeLockState = {
                    enabled: true,
                    sentinel: null,
                    pending: false,
                    status: 5,
                    listenersInstalled: false
                };
            }
            if (!state.enabled) {
                state.status = 4;
                return 0;
            }
            if (!navigator.wakeLock || typeof navigator.wakeLock.request !== "function") {
                state.status = 2;
                return 0;
            }
            if (document.visibilityState !== "visible") {
                state.status = 5;
                return 0;
            }
            if (state.sentinel || state.pending) return 1;

            state.pending = true;
            state.status = 5;
            navigator.wakeLock.request("screen").then(function (sentinel) {
                state.pending = false;
                if (!state.enabled || document.visibilityState !== "visible") {
                    try { sentinel.release(); } catch (releaseError) { }
                    state.status = state.enabled ? 5 : 4;
                    return;
                }
                state.sentinel = sentinel;
                state.status = 1; // active
                sentinel.addEventListener("release", function () {
                    state.sentinel = null;
                    state.pending = false;
                    if (!state.enabled) {
                        state.status = 4;
                    } else if (document.visibilityState === "visible") {
                        // Surface the revocation without retrying in a tight
                        // loop. The next real user gesture or visibility
                        // transition is the safe retry point, especially when
                        // the browser revoked the lock for low-battery mode.
                        state.status = 3;
                    } else {
                        state.status = 5;
                    }
                }, false);
            }).catch(function () {
                state.pending = false;
                state.sentinel = null;
                state.status = 3; // denied/revoked/error
            });
            return 1;
        } catch (error) {
            var failedState = window.__ratHabitatWakeLockState;
            if (failedState) {
                failedState.pending = false;
                failedState.status = 3;
            }
            return 0;
        }
    },

    RatHabitatBrowserWakeLockGetStatus: function () {
        try {
            var state = window.__ratHabitatWakeLockState;
            if (!state) return 4;
            return state.status || 4;
        } catch (error) {
            return 3;
        }
    },

    RatHabitatBrowserWakeLockRegisterLifecycle: function () {
        try {
            var state = window.__ratHabitatWakeLockState;
            if (!state) {
                state = window.__ratHabitatWakeLockState = {
                    enabled: true,
                    sentinel: null,
                    pending: false,
                    status: 5,
                    listenersInstalled: false
                };
            }
            if (state.listenersInstalled) return;
            state.listenersInstalled = true;
            window.__ratHabitatRequestWakeLock = function () {
                try {
                    if (window.__ratHabitatWakeLockState &&
                        window.__ratHabitatWakeLockState.enabled &&
                        document.visibilityState === "visible") {
                        // Invoke the bridge function without requiring a fake
                        // input event. This is used only to reacquire a lock
                        // that the browser revoked after visibility changed.
                        var current = window.__ratHabitatWakeLockState;
                        if (!current.sentinel && !current.pending && navigator.wakeLock && navigator.wakeLock.request) {
                            current.pending = true;
                            current.status = 5;
                            navigator.wakeLock.request("screen").then(function (sentinel) {
                                current.pending = false;
                                if (!current.enabled || document.visibilityState !== "visible") {
                                    try { sentinel.release(); } catch (releaseError) { }
                                    current.status = current.enabled ? 5 : 4;
                                    return;
                                }
                                current.sentinel = sentinel;
                                current.status = 1;
                                sentinel.addEventListener("release", function () {
                                    current.sentinel = null;
                                    current.pending = false;
                                    current.status = current.enabled ? 5 : 4;
                                }, false);
                            }).catch(function () {
                                current.pending = false;
                                current.sentinel = null;
                                current.status = 3;
                            });
                        }
                    }
                } catch (error) { }
            };
            document.addEventListener("visibilitychange", function () {
                if (document.visibilityState === "hidden") {
                    if (state.sentinel) {
                        try { state.sentinel.release(); } catch (releaseError) { }
                        state.sentinel = null;
                    }
                    state.pending = false;
                    state.status = state.enabled ? 5 : 4;
                } else if (state.enabled) {
                    window.setTimeout(window.__ratHabitatRequestWakeLock, 150);
                }
            }, false);
        } catch (error) { }
    }
});

// Browser-only bridge for the stable Rat Empire save record and the optional
// Screen Wake Lock. None of the browser callbacks call back into Unity. The
// bridge is intentionally synchronous for localStorage and one-way for the
// asynchronous Wake Lock Promise so it cannot form a Unity/JavaScript loop.
function ratHabitatSaveBridgeState() {
    if (!window.__ratHabitatSaveBridgeState) {
        window.__ratHabitatSaveBridgeState = {
            readInProgress: false,
            writeInProgress: false,
            removeInProgress: false,
            flushInProgress: false
        };
    }
    return window.__ratHabitatSaveBridgeState;
}

function ratHabitatWakeLockState() {
    var state = window.__ratHabitatWakeLockState;
    if (!state) {
        state = window.__ratHabitatWakeLockState = {
            enabled: true,
            sentinel: null,
            pending: false,
            releaseInProgress: false,
            reacquireTimer: null,
            reacquireOnVisible: false,
            status: 5,
            listenersInstalled: false
        };
    }
    return state;
}

function ratHabitatReleaseWakeLock(state) {
    if (!state || !state.sentinel || state.releaseInProgress) return;
    var sentinel = state.sentinel;
    state.sentinel = null;
    state.releaseInProgress = true;
    try {
        var releaseResult = sentinel.release();
        // A browser may return a Promise here. Handle rejection locally; it
        // must never re-enter Unity or create another request.
        if (releaseResult && typeof releaseResult.catch === "function") {
            releaseResult.catch(function () { });
        }
    } catch (error) {
    } finally {
        state.releaseInProgress = false;
    }
}

function ratHabitatInstallWakeLockRequest() {
    var state = ratHabitatWakeLockState();
    if (typeof window.__ratHabitatRequestWakeLock === "function") return state;

    window.__ratHabitatRequestWakeLock = function () {
        var current = ratHabitatWakeLockState();
        try {
            if (!current.enabled) {
                current.status = 4;
                return false;
            }
            if (!navigator.wakeLock || typeof navigator.wakeLock.request !== "function") {
                current.status = 2;
                return false;
            }
            if (document.visibilityState !== "visible") {
                current.status = 5;
                return false;
            }
            if (current.sentinel || current.pending) return true;

            current.pending = true;
            current.status = 5;
            navigator.wakeLock.request("screen").then(function (sentinel) {
                current.pending = false;
                if (!current.enabled || document.visibilityState !== "visible") {
                    // The request completed after the tab became hidden or
                    // the preference was disabled. Release only this sentinel.
                    try {
                        var result = sentinel.release();
                        if (result && typeof result.catch === "function") result.catch(function () { });
                    } catch (releaseError) { }
                    current.status = current.enabled ? 5 : 4;
                    return;
                }
                current.sentinel = sentinel;
                current.reacquireOnVisible = true;
                current.status = 1;
                sentinel.addEventListener("release", function () {
                    current.sentinel = null;
                    current.pending = false;
                    current.status = current.enabled ? 3 : 4;
                    // A browser revocation is reported, not immediately
                    // retried. The next user gesture or visibility transition
                    // is the safe retry point.
                }, false);
            }, function () {
                current.pending = false;
                current.sentinel = null;
                current.status = 3;
            });
            return true;
        } catch (error) {
            current.pending = false;
            current.sentinel = null;
            current.status = 3;
            return false;
        }
    };
    return state;
}

mergeInto(LibraryManager.library, {
    RatHabitatBrowserRead: function (keyPtr) {
        var bridge = ratHabitatSaveBridgeState();
        if (bridge.readInProgress) return allocateUTF8("");
        bridge.readInProgress = true;
        try {
            var key = UTF8ToString(keyPtr);
            var value = window.localStorage.getItem(key);
            return allocateUTF8(value === null ? "" : value);
        } catch (error) {
            console.warn("Rat Habitat browser save read failed", error);
            return allocateUTF8("");
        } finally {
            bridge.readInProgress = false;
        }
    },

    RatHabitatBrowserWrite: function (keyPtr, valuePtr) {
        var bridge = ratHabitatSaveBridgeState();
        if (bridge.writeInProgress) return 0;
        bridge.writeInProgress = true;
        try {
            var key = UTF8ToString(keyPtr);
            var value = UTF8ToString(valuePtr);
            window.localStorage.setItem(key, value);
            return 1;
        } catch (error) {
            console.error("Rat Habitat browser save write failed", error);
            return 0;
        } finally {
            bridge.writeInProgress = false;
        }
    },

    RatHabitatBrowserRemove: function (keyPtr) {
        var bridge = ratHabitatSaveBridgeState();
        if (bridge.removeInProgress) return;
        bridge.removeInProgress = true;
        try {
            var key = UTF8ToString(keyPtr);
            window.localStorage.removeItem(key);
        } catch (error) {
            console.warn("Rat Habitat browser save remove failed", error);
        } finally {
            bridge.removeInProgress = false;
        }
    },

    RatHabitatBrowserFlush: function (keyPtr) {
        var bridge = ratHabitatSaveBridgeState();
        if (bridge.flushInProgress) return;
        bridge.flushInProgress = true;
        try {
            var key = UTF8ToString(keyPtr);
            // localStorage is synchronous. Re-setting the last serialized
            // payload makes the requested flush explicit without invoking
            // another Unity save or relying on sessionStorage.
            var value = window.localStorage.getItem(key);
            if (value !== null) window.localStorage.setItem(key, value);
        } catch (error) {
            console.warn("Rat Habitat browser save flush failed", error);
        } finally {
            bridge.flushInProgress = false;
        }
    },

    RatHabitatBrowserRegisterLifecycle: function (keyPtr) {
        var key = UTF8ToString(keyPtr);
        try {
            if (!window.__ratHabitatSaveLifecycleKeys) window.__ratHabitatSaveLifecycleKeys = {};
            if (window.__ratHabitatSaveLifecycleKeys[key]) return;
            window.__ratHabitatSaveLifecycleKeys[key] = true;

            var flush = function () {
                var bridge = ratHabitatSaveBridgeState();
                if (bridge.flushInProgress) return;
                bridge.flushInProgress = true;
                try {
                    var value = window.localStorage.getItem(key);
                    if (value !== null) window.localStorage.setItem(key, value);
                } catch (error) {
                    // Unload handlers must never block page navigation.
                } finally {
                    bridge.flushInProgress = false;
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

    // The Unity side receives only compact status integers. Promise handlers
    // update browser state and never invoke a Unity callback.
    RatHabitatBrowserWakeLockSetDesired: function (enabled) {
        try {
            var state = ratHabitatWakeLockState();
            state.enabled = !!enabled;
            if (!state.enabled) {
                if (state.reacquireTimer !== null) {
                    window.clearTimeout(state.reacquireTimer);
                    state.reacquireTimer = null;
                }
                state.pending = false;
                state.reacquireOnVisible = false;
                state.status = 4;
                ratHabitatReleaseWakeLock(state);
                return;
            }
            if (!navigator.wakeLock || typeof navigator.wakeLock.request !== "function") {
                state.status = 2;
            } else if (document.visibilityState !== "visible") {
                state.status = 5;
            } else if (!state.sentinel && !state.pending) {
                // Initial acquisition remains user-gesture driven.
                state.status = 5;
            }
        } catch (error) {
        }
    },

    RatHabitatBrowserWakeLockRequest: function () {
        try {
            ratHabitatInstallWakeLockRequest();
            return window.__ratHabitatRequestWakeLock() ? 1 : 0;
        } catch (error) {
            var state = window.__ratHabitatWakeLockState;
            if (state) {
                state.pending = false;
                state.status = 3;
            }
            return 0;
        }
    },

    RatHabitatBrowserWakeLockGetStatus: function () {
        try {
            var state = window.__ratHabitatWakeLockState;
            return state ? (state.status || 4) : 4;
        } catch (error) {
            return 3;
        }
    },

    RatHabitatBrowserWakeLockRegisterLifecycle: function () {
        try {
            var state = ratHabitatInstallWakeLockRequest();
            if (state.listenersInstalled) return;
            state.listenersInstalled = true;

            document.addEventListener("visibilitychange", function () {
                var current = ratHabitatWakeLockState();
                if (document.visibilityState === "hidden") {
                    if (current.reacquireTimer !== null) {
                        window.clearTimeout(current.reacquireTimer);
                        current.reacquireTimer = null;
                    }
                    current.reacquireOnVisible = !!current.sentinel || current.reacquireOnVisible;
                    ratHabitatReleaseWakeLock(current);
                    current.pending = false;
                    current.status = current.enabled ? 5 : 4;
                } else if (current.enabled && current.reacquireOnVisible && current.reacquireTimer === null) {
                    // Wait briefly for the page to become fully visible. The
                    // guard allows at most one pending reacquisition.
                    current.reacquireTimer = window.setTimeout(function () {
                        current.reacquireTimer = null;
                        if (current.enabled && document.visibilityState === "visible") {
                            window.__ratHabitatRequestWakeLock();
                        }
                    }, 150);
                }
            }, false);
        } catch (error) {
        }
    }
});

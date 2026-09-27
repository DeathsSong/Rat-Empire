// Browser-only bridge for the stable Rat Empire save record and optional
// Screen Wake Lock. Unity/Emscripten may omit top-level .jslib declarations,
// so every exported entry point lazily installs the helpers it uses onto a
// uniquely named window namespace before calling them.

mergeInto(LibraryManager.library, {
    RatHabitatBrowserRead: function (keyPtr) {
        if (!window.__ratHabitatGetSaveBridgeState) {
            window.__ratHabitatGetSaveBridgeState = function () {
                if (!window.__ratHabitatSaveBridgeState) {
                    window.__ratHabitatSaveBridgeState = {
                        readInProgress: false,
                        writeInProgress: false,
                        removeInProgress: false,
                        flushInProgress: false
                    };
                }
                return window.__ratHabitatSaveBridgeState;
            };
        }
        var bridge = window.__ratHabitatGetSaveBridgeState();
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
        if (!window.__ratHabitatGetSaveBridgeState) {
            window.__ratHabitatGetSaveBridgeState = function () {
                if (!window.__ratHabitatSaveBridgeState) {
                    window.__ratHabitatSaveBridgeState = {
                        readInProgress: false,
                        writeInProgress: false,
                        removeInProgress: false,
                        flushInProgress: false
                    };
                }
                return window.__ratHabitatSaveBridgeState;
            };
        }
        var bridge = window.__ratHabitatGetSaveBridgeState();
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
        if (!window.__ratHabitatGetSaveBridgeState) {
            window.__ratHabitatGetSaveBridgeState = function () {
                if (!window.__ratHabitatSaveBridgeState) {
                    window.__ratHabitatSaveBridgeState = {
                        readInProgress: false,
                        writeInProgress: false,
                        removeInProgress: false,
                        flushInProgress: false
                    };
                }
                return window.__ratHabitatSaveBridgeState;
            };
        }
        var bridge = window.__ratHabitatGetSaveBridgeState();
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
        if (!window.__ratHabitatGetSaveBridgeState) {
            window.__ratHabitatGetSaveBridgeState = function () {
                if (!window.__ratHabitatSaveBridgeState) {
                    window.__ratHabitatSaveBridgeState = {
                        readInProgress: false,
                        writeInProgress: false,
                        removeInProgress: false,
                        flushInProgress: false
                    };
                }
                return window.__ratHabitatSaveBridgeState;
            };
        }
        var bridge = window.__ratHabitatGetSaveBridgeState();
        if (bridge.flushInProgress) return;
        bridge.flushInProgress = true;
        try {
            var key = UTF8ToString(keyPtr);
            // localStorage is synchronous. Re-setting the current payload
            // makes the flush explicit without re-entering Unity.
            var value = window.localStorage.getItem(key);
            if (value !== null) window.localStorage.setItem(key, value);
        } catch (error) {
            console.warn("Rat Habitat browser save flush failed", error);
        } finally {
            bridge.flushInProgress = false;
        }
    },

    RatHabitatBrowserRegisterLifecycle: function (keyPtr) {
        if (!window.__ratHabitatGetSaveBridgeState) {
            window.__ratHabitatGetSaveBridgeState = function () {
                if (!window.__ratHabitatSaveBridgeState) {
                    window.__ratHabitatSaveBridgeState = {
                        readInProgress: false,
                        writeInProgress: false,
                        removeInProgress: false,
                        flushInProgress: false
                    };
                }
                return window.__ratHabitatSaveBridgeState;
            };
        }
        var key = UTF8ToString(keyPtr);
        try {
            if (!window.__ratHabitatSaveLifecycleKeys) window.__ratHabitatSaveLifecycleKeys = {};
            if (window.__ratHabitatSaveLifecycleKeys[key]) return;
            window.__ratHabitatSaveLifecycleKeys[key] = true;

            var flush = function () {
                var bridge = window.__ratHabitatGetSaveBridgeState();
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

    RatHabitatBrowserWakeLockSetDesired: function (enabled) {
        if (!window.__ratHabitatGetWakeLockState) {
            window.__ratHabitatGetWakeLockState = function () {
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
            };
        }
        if (!window.__ratHabitatReleaseWakeLock) {
            window.__ratHabitatReleaseWakeLock = function (state) {
                if (!state || !state.sentinel || state.releaseInProgress) return;
                var sentinel = state.sentinel;
                state.sentinel = null;
                state.releaseInProgress = true;
                try {
                    var result = sentinel.release();
                    if (result && typeof result.catch === "function") result.catch(function () { });
                } catch (error) {
                } finally {
                    state.releaseInProgress = false;
                }
            };
        }
        if (!window.__ratHabitatInstallWakeLockRequest) {
            window.__ratHabitatInstallWakeLockRequest = function () {
                var state = window.__ratHabitatGetWakeLockState();
                if (window.__ratHabitatWakeLockBridgeVersion === 2 &&
                    typeof window.__ratHabitatRequestWakeLock === "function") return state;
                window.__ratHabitatRequestWakeLock = function () {
                    var current = window.__ratHabitatGetWakeLockState();
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
                window.__ratHabitatWakeLockBridgeVersion = 2;
                return state;
            };
        }
        try {
            var state = window.__ratHabitatGetWakeLockState();
            state.enabled = !!enabled;
            if (!state.enabled) {
                if (state.reacquireTimer !== null) {
                    window.clearTimeout(state.reacquireTimer);
                    state.reacquireTimer = null;
                }
                state.pending = false;
                state.reacquireOnVisible = false;
                state.status = 4;
                window.__ratHabitatReleaseWakeLock(state);
                return;
            }
            if (!navigator.wakeLock || typeof navigator.wakeLock.request !== "function") {
                state.status = 2;
            } else if (document.visibilityState !== "visible") {
                state.status = 5;
            } else if (!state.sentinel && !state.pending) {
                state.status = 5;
            }
        } catch (error) {
        }
    },

    RatHabitatBrowserWakeLockRequest: function () {
        // Install the same namespaced helpers here too: this export can be
        // the first bridge entry point on a fresh page.
        if (!window.__ratHabitatGetWakeLockState) {
            window.__ratHabitatGetWakeLockState = function () {
                var state = window.__ratHabitatWakeLockState;
                if (!state) {
                    state = window.__ratHabitatWakeLockState = {
                        enabled: true, sentinel: null, pending: false,
                        releaseInProgress: false, reacquireTimer: null,
                        reacquireOnVisible: false, status: 5,
                        listenersInstalled: false
                    };
                }
                return state;
            };
        }
        if (!window.__ratHabitatReleaseWakeLock) {
            window.__ratHabitatReleaseWakeLock = function (state) {
                if (!state || !state.sentinel || state.releaseInProgress) return;
                var sentinel = state.sentinel;
                state.sentinel = null;
                state.releaseInProgress = true;
                try {
                    var result = sentinel.release();
                    if (result && typeof result.catch === "function") result.catch(function () { });
                } catch (error) {
                } finally {
                    state.releaseInProgress = false;
                }
            };
        }
        if (!window.__ratHabitatInstallWakeLockRequest) {
            window.__ratHabitatInstallWakeLockRequest = function () {
                var state = window.__ratHabitatGetWakeLockState();
                if (window.__ratHabitatWakeLockBridgeVersion === 2 && typeof window.__ratHabitatRequestWakeLock === "function") return state;
                window.__ratHabitatRequestWakeLock = function () {
                    var current = window.__ratHabitatGetWakeLockState();
                    try {
                        if (!current.enabled) { current.status = 4; return false; }
                        if (!navigator.wakeLock || typeof navigator.wakeLock.request !== "function") { current.status = 2; return false; }
                        if (document.visibilityState !== "visible") { current.status = 5; return false; }
                        if (current.sentinel || current.pending) return true;
                        current.pending = true;
                        current.status = 5;
                        navigator.wakeLock.request("screen").then(function (sentinel) {
                            current.pending = false;
                            if (!current.enabled || document.visibilityState !== "visible") {
                                try { var result = sentinel.release(); if (result && typeof result.catch === "function") result.catch(function () { }); } catch (error) { }
                                current.status = current.enabled ? 5 : 4;
                                return;
                            }
                            current.sentinel = sentinel;
                            current.reacquireOnVisible = true;
                            current.status = 1;
                            sentinel.addEventListener("release", function () { current.sentinel = null; current.pending = false; current.status = current.enabled ? 3 : 4; }, false);
                        }, function () { current.pending = false; current.sentinel = null; current.status = 3; });
                        return true;
                    } catch (error) { current.pending = false; current.sentinel = null; current.status = 3; return false; }
                };
                window.__ratHabitatWakeLockBridgeVersion = 2;
                return state;
            };
        }
        try {
            window.__ratHabitatInstallWakeLockRequest();
            return window.__ratHabitatRequestWakeLock() ? 1 : 0;
        } catch (error) {
            var state = window.__ratHabitatWakeLockState;
            if (state) { state.pending = false; state.status = 3; }
            return 0;
        }
    },

    RatHabitatBrowserWakeLockGetStatus: function () {
        if (!window.__ratHabitatGetWakeLockState) {
            window.__ratHabitatGetWakeLockState = function () {
                var state = window.__ratHabitatWakeLockState;
                if (!state) state = window.__ratHabitatWakeLockState = { enabled: true, sentinel: null, pending: false, releaseInProgress: false, reacquireTimer: null, reacquireOnVisible: false, status: 5, listenersInstalled: false };
                return state;
            };
        }
        try {
            var state = window.__ratHabitatGetWakeLockState();
            return state ? (state.status || 4) : 4;
        } catch (error) {
            return 3;
        }
    },

    RatHabitatBrowserWakeLockRegisterLifecycle: function () {
        // Install all wake helpers here too: lifecycle registration normally
        // happens before SetDesired and before a user-gesture request.
        if (!window.__ratHabitatGetWakeLockState) {
            window.__ratHabitatGetWakeLockState = function () {
                var state = window.__ratHabitatWakeLockState;
                if (!state) state = window.__ratHabitatWakeLockState = { enabled: true, sentinel: null, pending: false, releaseInProgress: false, reacquireTimer: null, reacquireOnVisible: false, status: 5, listenersInstalled: false };
                return state;
            };
        }
        if (!window.__ratHabitatReleaseWakeLock) {
            window.__ratHabitatReleaseWakeLock = function (state) {
                if (!state || !state.sentinel || state.releaseInProgress) return;
                var sentinel = state.sentinel;
                state.sentinel = null;
                state.releaseInProgress = true;
                try { var result = sentinel.release(); if (result && typeof result.catch === "function") result.catch(function () { }); } catch (error) { } finally { state.releaseInProgress = false; }
            };
        }
        if (!window.__ratHabitatInstallWakeLockRequest) {
            window.__ratHabitatInstallWakeLockRequest = function () {
                var state = window.__ratHabitatGetWakeLockState();
                if (window.__ratHabitatWakeLockBridgeVersion === 2 && typeof window.__ratHabitatRequestWakeLock === "function") return state;
                window.__ratHabitatRequestWakeLock = function () {
                    var current = window.__ratHabitatGetWakeLockState();
                    try {
                        if (!current.enabled) { current.status = 4; return false; }
                        if (!navigator.wakeLock || typeof navigator.wakeLock.request !== "function") { current.status = 2; return false; }
                        if (document.visibilityState !== "visible") { current.status = 5; return false; }
                        if (current.sentinel || current.pending) return true;
                        current.pending = true; current.status = 5;
                        navigator.wakeLock.request("screen").then(function (sentinel) {
                            current.pending = false;
                            if (!current.enabled || document.visibilityState !== "visible") {
                                try { var result = sentinel.release(); if (result && typeof result.catch === "function") result.catch(function () { }); } catch (error) { }
                                current.status = current.enabled ? 5 : 4; return;
                            }
                            current.sentinel = sentinel; current.reacquireOnVisible = true; current.status = 1;
                            sentinel.addEventListener("release", function () { current.sentinel = null; current.pending = false; current.status = current.enabled ? 3 : 4; }, false);
                        }, function () { current.pending = false; current.sentinel = null; current.status = 3; });
                        return true;
                    } catch (error) { current.pending = false; current.sentinel = null; current.status = 3; return false; }
                };
                window.__ratHabitatWakeLockBridgeVersion = 2;
                return state;
            };
        }
        try {
            var state = window.__ratHabitatInstallWakeLockRequest();
            if (state.listenersInstalled) return;
            state.listenersInstalled = true;
            document.addEventListener("visibilitychange", function () {
                var current = window.__ratHabitatGetWakeLockState();
                if (document.visibilityState === "hidden") {
                    if (current.reacquireTimer !== null) { window.clearTimeout(current.reacquireTimer); current.reacquireTimer = null; }
                    current.reacquireOnVisible = !!current.sentinel || current.reacquireOnVisible;
                    window.__ratHabitatReleaseWakeLock(current);
                    current.pending = false; current.status = current.enabled ? 5 : 4;
                } else if (current.enabled && current.reacquireOnVisible && current.reacquireTimer === null) {
                    current.reacquireTimer = window.setTimeout(function () {
                        current.reacquireTimer = null;
                        if (current.enabled && document.visibilityState === "visible") window.__ratHabitatRequestWakeLock();
                    }, 150);
                }
            }, false);
        } catch (error) {
        }
    }
});

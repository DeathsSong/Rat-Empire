using System.Runtime.InteropServices;

namespace RatHabitat
{
    /// <summary>
    /// Owns the optional browser Screen Wake Lock preference and the small
    /// WebGL bridge used to request/reacquire the browser sentinel. The
    /// preference is saved with the colony; the browser lock itself is always
    /// treated as temporary and may be revoked by the browser at any time.
    /// </summary>
    public static class BrowserWakeLockSystem
    {
        public const int StatusUnknown = 0;
        public const int StatusActive = 1;
        public const int StatusUnsupported = 2;
        public const int StatusDenied = 3;
        public const int StatusDisabled = 4;
        public const int StatusWaiting = 5;

        private static bool initialized;
        private static bool desiredEnabled = true;
        private static int lastStatus = StatusUnknown;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void RatHabitatBrowserWakeLockSetDesired(int enabled);

        [DllImport("__Internal")]
        private static extern int RatHabitatBrowserWakeLockRequest();

        [DllImport("__Internal")]
        private static extern int RatHabitatBrowserWakeLockGetStatus();

        [DllImport("__Internal")]
        private static extern void RatHabitatBrowserWakeLockRegisterLifecycle();
#endif

        public static bool IsEnabled(ColonySaveData save)
        {
            return save == null ? desiredEnabled : save.keepScreenAwake;
        }

        public static int Status
        {
            get { return lastStatus; }
        }

        public static void Initialize(ColonySaveData save)
        {
            if (save != null)
            {
                save.EnsureLists();
                desiredEnabled = save.keepScreenAwake;
            }
            initialized = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            RatHabitatBrowserWakeLockRegisterLifecycle();
            RatHabitatBrowserWakeLockSetDesired(desiredEnabled ? 1 : 0);
            lastStatus = RatHabitatBrowserWakeLockGetStatus();
#else
            // Native builds do not expose the browser API. The game remains
            // fully playable; Settings simply explains that this is a WebGL
            // browser feature.
            lastStatus = StatusUnsupported;
#endif
        }

        public static bool SetPreference(ColonySaveData save, bool enabled, bool requestNow)
        {
            if (save == null) return false;
            save.keepScreenAwake = enabled;
            save.keepScreenAwakePreferenceInitialized = true;
            desiredEnabled = enabled;
            initialized = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            RatHabitatBrowserWakeLockSetDesired(enabled ? 1 : 0);
            if (enabled && requestNow) RatHabitatBrowserWakeLockRequest();
            lastStatus = RatHabitatBrowserWakeLockGetStatus();
#else
            lastStatus = enabled ? StatusUnsupported : StatusDisabled;
#endif
            return true;
        }

        /// <summary>
        /// Called from an actual Unity UI/world gesture. This is the only
        /// normal gameplay path that requests a new lock after startup.
        /// </summary>
        public static void RequestFromUserGesture(ColonySaveData save)
        {
            if (!initialized) Initialize(save);
            if (!IsEnabled(save)) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            RatHabitatBrowserWakeLockRequest();
            lastStatus = RatHabitatBrowserWakeLockGetStatus();
#else
            lastStatus = StatusUnsupported;
#endif
        }

        /// <summary>
        /// Polls the async browser result without touching simulation state.
        /// Returns true only when Settings text may need to be refreshed.
        /// </summary>
        public static bool PollStatus()
        {
            if (!initialized) return false;
            int status = lastStatus;
#if UNITY_WEBGL && !UNITY_EDITOR
            status = RatHabitatBrowserWakeLockGetStatus();
#endif
            if (status == lastStatus) return false;
            lastStatus = status;
            return true;
        }

        public static string StatusMessage(ColonySaveData save)
        {
            if (!IsEnabled(save)) return "Keep Screen Awake is off.";
            switch (lastStatus)
            {
                case StatusActive:
                    return "The screen will stay awake while Rat Empire is active.";
                case StatusWaiting:
                    return "Screen wake lock is waiting for the game to be visible or tapped.";
                case StatusUnsupported:
                    return "This browser does not support Screen Wake Lock. You may need to change the device display-timeout setting manually.";
                case StatusDenied:
                    return "The browser denied or revoked Screen Wake Lock. You may need to change the device display-timeout setting manually.";
                default:
                    return "Screen wake lock will start after you tap the game.";
            }
        }
    }
}

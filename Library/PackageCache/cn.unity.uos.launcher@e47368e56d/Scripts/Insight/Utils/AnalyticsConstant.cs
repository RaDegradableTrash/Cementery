using System.Collections.Generic;

namespace Unity.UOS.Insight.Utils
{
    public delegate void ResponseHandle(Dictionary<string, object> result = null);

    public static class AnalyticsConstant
    {
        // event time
        public const string TIME = "time";

        // user ID
        public const string USER_ID = "user_id";

        // distinct ID
        public const string DISTINCT_ID = "#distinct_id";

        // event name
        public const string EVENT_NAME = "event_name";

        // account ID
        public const string ACCOUNT_ID = "#account_id";

        // event properties
        public const string PROPERTIES = "properties";

        // network type
        public const string NETWORK_TYPE = "#network_type";

        // sdk version
        public const string LIB_VERSION = "#lib_version";

        // carrier name
        public const string CARRIER = "#carrier";

        // sdk name
        public const string LIB = "#lib";

        // os name
        public const string OS = "#os";

        // device ID
        public const string DEVICE_ID = "#device_id";

        // device AD ID
        public const string DEVICE_AD_ID = "#device_ad_id";

        // device screen height
        public const string SCREEN_HEIGHT = "#screen_height";

        //device screen width
        public const string SCREEN_WIDTH = "#screen_width";

        // device manufacturer
        public const string MANUFACTURE = "#manufacturer";

        // device model
        public const string DEVICE_MODEL = "#device_model";

        // device system language
        public const string SYSTEM_LANGUAGE = "#system_language";

        // os version
        public const string OS_VERSION = "#os_version";

        // app version
        public const string APP_VERSION = "#app_version";

        // app bundle ID
        public const string APP_BUNDLE_ID = "#bundle_id";

        // zone offset
        public const string ZONE_OFFSET = "#zone_offset";

        // unique ID for the event
        public const string UUID = "#uuid";

        // special event ID
        public const string EVENT_ID = "#event_id";

        // random ID
        public const string RANDOM_ID = "RANDDOM_ID";

        // random ID(WebGL)
        public const string RANDOM_DEVICE_ID = "RANDOM_DEVICE_ID";

        // event duration
        public const string DURATION = "#duration";

        // flush time
        public static readonly string FLUSH_TIME = "#flush_time";

        // request data
        public static readonly string REQUEST_DATA = "data";

        // super properties
        public const string SUPER_PROPERTY = "super_properties";

        // Whether to pause data reporting
        public const string ENABLE_REPORT = "enable_report";

        // Whether to stop data reporting
        public const string OPT_REPORT = "opt_report";

        // Whether the installation is recorded
        public const string IS_INSTALL = "is_install";

        // app install event
        public const string INSTALL_EVENT = "app_install";

        // app start event
        public const string START_EVENT = "app_start";

        // app end event
        public const string END_EVENT = "app_end";

        // app crash event
        public static readonly string CRASH_EVENT = "app_crash";

        // app crash reason
        public static readonly string CRASH_REASON = "#app_crashed_reason";

        // scene load
        public static readonly string APP_SCENE_LOAD = "scene_loaded";

        // scene unload
        public static readonly string APP_SCENE_UNLOAD = "scene_unloaded";
    }
}
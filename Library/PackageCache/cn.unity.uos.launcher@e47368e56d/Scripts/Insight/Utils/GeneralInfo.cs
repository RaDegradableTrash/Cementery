using UnityEngine;

namespace Unity.UOS.Insight.Utils
{
    public static class GeneralInfo
    {
        // sdk version
        public static string LibVersion()
        {
            return InsightSDK.LIB_VERSION;
        }

        // sdk name
        public static string LibName()
        {
            return InsightSDK.LIB_NAME;
        }

        // app version
        public static string AppVersion()
        {
            return Application.version;
        }

        // app identifier, bundle ID
        public static string AppIdentifier()
        {
            return Application.identifier;
        }
    }
}
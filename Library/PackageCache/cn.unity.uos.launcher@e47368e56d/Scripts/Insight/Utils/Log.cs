using UnityEngine;

namespace Unity.UOS.Insight.Utils
{
    public class Log
    {
        private static bool enableLog;
        public static void EnableLog(bool enabled)
        {
            enableLog = enabled;
        }

        public static bool GetEnable()
        {
            return enableLog;
        }


        public static void i(string message)
        {
            if (enableLog)
            {
                Debug.Log("[UOS Insight] Info: " + message);
            }
        }

        public static void d(string message)
        {
            if (enableLog)
            {
                Debug.Log("[UOS Insight] Debug: " + message);
            }
        }

        public static void e(string message)
        {
            if (enableLog)
            {
                Debug.LogError("[UOS Insight] Error: " + message);
            }
        }

        public static void w(string message)
        {
            if (enableLog)
            {
                Debug.LogWarning("[UOS Insight] Warning: " + message);
            }
        }
    }
}
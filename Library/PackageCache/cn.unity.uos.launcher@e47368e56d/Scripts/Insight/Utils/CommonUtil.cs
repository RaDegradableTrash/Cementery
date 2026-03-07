using System;
using System.Collections.Generic;

namespace Unity.UOS.Insight.Utils
{
    public static class CommonUtil
    {
        private static Dictionary<string, object> deviceInfo = null;

        /*
         * Check if the URL is valid
         */
        public static bool IsValidURL(string url)
        {
            return !(string.IsNullOrEmpty(url) || !url.Contains("http") || !url.Contains("https"));
        }

        /*
         * Check if the string is empty
         */
        public static bool IsEmptyString(string str)
        {
            return string.IsNullOrEmpty(str);
        }

        public static Dictionary<string, object> GetDeviceInfo()
        {
            if (deviceInfo == null)
            {
                deviceInfo = new Dictionary<string, object>();
                deviceInfo[AnalyticsConstant.DEVICE_ID] = DeviceInfo.DeviceID();
                //deviceInfo[AnalyticsConstant.CARRIER] = DeviceInfo.Carrier(); //PC????
                deviceInfo[AnalyticsConstant.OS] = DeviceInfo.OS();
                deviceInfo[AnalyticsConstant.OS_VERSION] = DeviceInfo.OSVersion();
                deviceInfo[AnalyticsConstant.SCREEN_HEIGHT] = DeviceInfo.ScreenHeight();
                deviceInfo[AnalyticsConstant.SCREEN_WIDTH] = DeviceInfo.ScreenWidth();
                deviceInfo[AnalyticsConstant.MANUFACTURE] = DeviceInfo.Manufacture();
                deviceInfo[AnalyticsConstant.DEVICE_MODEL] = DeviceInfo.DeviceModel();
                deviceInfo[AnalyticsConstant.APP_VERSION] = GeneralInfo.AppVersion();
                deviceInfo[AnalyticsConstant.APP_BUNDLE_ID] = GeneralInfo.AppIdentifier();
                deviceInfo[AnalyticsConstant.LIB] = GeneralInfo.LibName();
                deviceInfo[AnalyticsConstant.LIB_VERSION] = GeneralInfo.LibVersion();
            }

            deviceInfo[AnalyticsConstant.SYSTEM_LANGUAGE] = DeviceInfo.MachineLanguage();
            deviceInfo[AnalyticsConstant.NETWORK_TYPE] = DeviceInfo.NetworkType();
            return deviceInfo;
        }

        // A persistent random number, used as an alternative to the distinct ID
        public static string RandomID(bool persistent = true)
        {
            string randomID = null;
            if (persistent)
            {
                randomID = (string)File.GetData(AnalyticsConstant.RANDOM_ID, typeof(string));
            }

            if (string.IsNullOrEmpty(randomID))
            {
                randomID = Guid.NewGuid().ToString("N");
                if (persistent)
                {
                    File.SaveData(AnalyticsConstant.RANDOM_ID, randomID);
                }
            }

            return randomID;
        }

        // Get time zone offset
        public static double ZoneOffset(DateTime dateTime, TimeZoneInfo timeZone)
        {
            return timeZone.BaseUtcOffset.TotalHours;
        }


        // add Dictionary to Dictionary
        public static void AddDictionary(Dictionary<string, object> originalDic, Dictionary<string, object> subDic)
        {
            if (originalDic != subDic)
            {
                foreach (KeyValuePair<string, object> kv in subDic)
                {
                    originalDic[kv.Key] = kv.Value;
                }
            }
        }

        //get timestamp
        public static long GetTimeStamp()
        {
            TimeSpan ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
            return Convert.ToInt64(ts.TotalMilliseconds);
        }

        public static string FormatDate(DateTime dateTime, TimeZoneInfo timeZone)
        {
            var targetTime = TimeZoneInfo.ConvertTime(dateTime, timeZone);
            return targetTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}

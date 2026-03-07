using System;
using UnityEngine;

namespace Unity.UOS.Insight.Utils
{
    public class DeviceInfo
    {
        // devide ID
        public static string DeviceID()
        {
#if (UNITY_WEBGL)
                return RandomDeviceID();
#else
            return SystemInfo.deviceUniqueIdentifier;
#endif
        }

        // A persistent random number, used as an alternative to the device ID (WebGL cannot obtain the device ID)
        private static string RandomDeviceID()
        {
            string randomID = (string)File.GetData(AnalyticsConstant.RANDOM_DEVICE_ID, typeof(string));
            if (string.IsNullOrEmpty(randomID))
            {
                randomID = Guid.NewGuid().ToString("N");
                File.SaveData(AnalyticsConstant.RANDOM_DEVICE_ID, randomID);
            }

            return randomID;
        }

        // network type
        public static string NetworkType()
        {
            string networkType = "NULL";
            if (Application.internetReachability == NetworkReachability.ReachableViaCarrierDataNetwork)
            {
                networkType = "Mobile";
            }
            else if (Application.internetReachability == NetworkReachability.ReachableViaLocalAreaNetwork)
            {
                networkType = "LAN";
            }

            return networkType;
        }

        // carrier name
        public static string Carrier()
        {
            return "NULL";
        }

        // os name
        public static string OS()
        {
            return SystemInfo.operatingSystemFamily switch
            {
                OperatingSystemFamily.Linux => "Linux",
                OperatingSystemFamily.MacOSX => "MacOSX",
                OperatingSystemFamily.Windows => "Windows",
                _ => "other"
            };
        }

        // os version
        public static string OSVersion()
        {
            return SystemInfo.operatingSystem;
        }

        // device screen width
        public static int ScreenWidth()
        {
            return Screen.currentResolution.width;
        }

        // device screen height
        public static int ScreenHeight()
        {
            return Screen.currentResolution.height;
        }

        // graphics card manufacturer name
        public static string Manufacture()
        {
            return SystemInfo.graphicsDeviceVendor;
        }

        // devide model
        public static string DeviceModel()
        {
            return SystemInfo.deviceModel;
        }

        // device language
        public static string MachineLanguage()
        {
            return Application.systemLanguage switch
            {
                SystemLanguage.Afrikaans => "af",
                SystemLanguage.Arabic => "ar",
                SystemLanguage.Basque => "eu",
                SystemLanguage.Belarusian => "be",
                SystemLanguage.Bulgarian => "bg",
                SystemLanguage.Catalan => "ca",
                SystemLanguage.Chinese => "zh",
                SystemLanguage.Czech => "cs",
                SystemLanguage.Danish => "da",
                SystemLanguage.Dutch => "nl",
                SystemLanguage.English => "en",
                SystemLanguage.Estonian => "et",
                SystemLanguage.Faroese => "fo",
                SystemLanguage.Finnish => "fu",
                SystemLanguage.French => "fr",
                SystemLanguage.German => "de",
                SystemLanguage.Greek => "el",
                SystemLanguage.Hebrew => "he",
                SystemLanguage.Icelandic => "is",
                SystemLanguage.Indonesian => "id",
                SystemLanguage.Italian => "it",
                SystemLanguage.Japanese => "ja",
                SystemLanguage.Korean => "ko",
                SystemLanguage.Latvian => "lv",
                SystemLanguage.Lithuanian => "lt",
                SystemLanguage.Norwegian => "nn",
                SystemLanguage.Polish => "pl",
                SystemLanguage.Portuguese => "pt",
                SystemLanguage.Romanian => "ro",
                SystemLanguage.Russian => "ru",
                SystemLanguage.SerboCroatian => "sr",
                SystemLanguage.Slovak => "sk",
                SystemLanguage.Slovenian => "sl",
                SystemLanguage.Spanish => "es",
                SystemLanguage.Swedish => "sv",
                SystemLanguage.Thai => "th",
                SystemLanguage.Turkish => "tr",
                SystemLanguage.Ukrainian => "uk",
                SystemLanguage.Vietnamese => "vi",
                SystemLanguage.ChineseSimplified => "zh",
                SystemLanguage.ChineseTraditional => "zh",
                SystemLanguage.Hungarian => "hu",
                SystemLanguage.Unknown => "unknown",
                _ => ""
            };
        }
    }
}


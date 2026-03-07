using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.UOS.Common;
using Unity.UOS.Insight.Config;
using Unity.UOS.Insight.Exceptions;
using Unity.UOS.Insight.Models;
using Unity.UOS.Insight.Service;
using Unity.UOS.Insight.Transport;
using Unity.UOS.Insight.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using Task = System.Threading.Tasks.Task;

namespace Unity.UOS.Insight
{
    /// <summary>
    /// UOS Insight SDK.
    /// </summary>
    [DisallowMultipleComponent]
    public partial class InsightSDK : MonoBehaviour
    {
        private static bool _initialized;
        private static bool _enabled;

        private static readonly Queue<Action> _operationCache = new();

        private static InsightSDK _instance;

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
                StartCoroutine(InitializeCoroutine());
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Init Insight SDK
        /// </summary>
        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _ = new GameObject("UOSInsight", typeof(InsightSDK));
            Log.EnableLog(false);
        }

        private static IEnumerator InitializeCoroutine()
        {
            var task = checkInsightServiceEnabled();
            while (!task.IsCompleted)
            {
                yield return null;
            }

            bool enabled = task.Result;
            if (!enabled)
            {
                //Debug.Log("service not enabled");
                _initialized = true;
                _enabled = false;
                _operationCache.Clear();
                yield break;
            }

            _enabled = true;

            DataAnalyticsConfig config = new DataAnalyticsConfig(Settings.AppID);
            InitService(config);

            var adIdTask = GetDeviceAdvertisingId();
            while (!adIdTask.IsCompleted)
            {
                yield return null;
            }

            FlushEventCaches();
        }

        private static async Task<bool> checkInsightServiceEnabled()
        {
            try
            {
                _ = await TransportClient.GetProject();
                return true;
            }
            catch (Exception e)
            {
                if (Log.GetEnable()) Log.e("Get service status failed : " + e.Message);
            }

            return false;
        }

        private static void InitService(DataAnalyticsConfig config)
        {
            _initialized = true;

            try
            {
                if (!string.IsNullOrEmpty(config.appId))
                {
                    config.appId = config.appId.Replace(" ", "");

                    InsightConfig insightConfig = InsightConfig.GetInstance(config.appId, config.name);
                    if (!string.IsNullOrEmpty(config.getTimeZoneId()))
                    {
                        try
                        {
                            insightConfig.SetTimeZone(TimeZoneInfo.FindSystemTimeZoneById(config.getTimeZoneId()));
                        }
                        catch (Exception e)
                        {
                            if (Log.GetEnable()) Log.e("TimeZoneInfo set failed : " + e.Message);
                        }
                    }

                    AnalyticsService.Init(config.appId, config.name, insightConfig, _instance);
                    if (Log.GetEnable())
                        Log.i(string.Format(
                            "Insight SDK initialize success, AppId = {0}, TimeZone = {1}, DeviceId = {2}, Lib = Unity, LibVersion = {3}{4}",
                            config.appId, config.timeZone, AnalyticsService.GetDeviceId(), LIB_VERSION,
                            (config.name != null ? (", Name = " + config.name) : "")));
                }
            }
            catch (Exception ex)
            {
                if (Log.GetEnable()) Log.i("Insight start Error: " + ex.Message);
            }
        }

        /// <summary>
        /// Enable auto-reporting
        /// </summary>
        /// <param name="eventType">auto-reporting eventType</param>
        /// <param name="properties">properties for auto-reporting events (optional)</param>
        public static void EnableAutoReport(AutoReportEventType eventType, Dictionary<string, object> properties = null)
        {
            if (!_initialized)
            {
                _operationCache.Enqueue(() => { EnableAutoReport(eventType, properties); });
                return;
            }

            if (!_enabled)
                return;

            properties ??= new Dictionary<string, object>();

            AnalyticsService.EnableAutoReport(eventType, properties);
            if ((eventType & AutoReportEventType.AppSceneLoad) != 0)
            {
                ReportSceneLoad(SceneManager.GetActiveScene());
            }

            if ((eventType & AutoReportEventType.AppCrash) != 0)
            {
                ExceptionHandler.RegisterExceptionHandler(properties);
            }

            if ((eventType & AutoReportEventType.AppSceneLoad) != 0)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                SceneManager.sceneLoaded += OnSceneLoaded;
            }

            if ((eventType & AutoReportEventType.AppSceneUnload) != 0)
            {
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
            }
        }


        /// <summary>
        /// Set properties for auto-reporting events
        /// </summary>
        /// <param name="eventType">auto-reporting eventType</param>
        /// <param name="properties">properties for auto-reporting events</param>
        public static void SetAutoReportProperties(AutoReportEventType eventType, Dictionary<string, object> properties)
        {
            if (!_initialized)
            {
                _operationCache.Enqueue(() => { SetAutoReportProperties(eventType, properties); });
                return;
            }

            if (!_enabled)
                return;

            AnalyticsService.SetAutoReportProperties(eventType, properties);
            if ((eventType & AutoReportEventType.AppCrash) != 0)
            {
                ExceptionHandler.SetAutoReportProperties(properties);
            }
        }

        /// <summary>
        /// Report an Event
        /// </summary>
        /// <param name="eventName">the event name</param>
        /// <param name="properties">properties for the event</param>
        public static void ReportEvent(string eventName, params (string key, object value)[] properties)
        {
            ReportEvent(eventName, properties.ToDictionary(tuple => tuple.key, tuple => tuple.value));
        }

        /// <summary>
        /// Report an Event
        /// </summary>
        /// <param name="eventName">the event name</param>
        /// <param name="properties">properties for the event</param>
        public static void ReportEvent(string eventName, Dictionary<string, object> properties = null)
        {
            if (!_initialized)
            {
                _operationCache.Enqueue(() => { ReportEvent(eventName, properties); });
                return;
            }

            if (!_enabled)
                return;

            AnalyticsService.Report(eventName, properties);
        }

        /// <summary>
        /// Report events data to server immediately
        /// </summary>
        public static void Flush()
        {
            if (!_initialized)
            {
                _operationCache.Enqueue(Flush);
                return;
            }

            if (!_enabled)
                return;

            AnalyticsService.Flush();
        }

        /// <summary>
        /// Scenes load Delegate
        /// </summary>
        /// <param name="scene">the load scene</param>
        /// <param name="mode">the scene loading mode</param>
        public static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!_initialized)
            {
                _operationCache.Enqueue(() => { OnSceneLoaded(scene, mode); });
                return;
            }

            if (!_enabled)
                return;

            ReportSceneLoad(scene);
        }

        /// <summary>
        /// Scenes unload Delegate
        /// </summary>
        /// <param name="scene">the unload scene</param>
        public static void OnSceneUnloaded(Scene scene)
        {
            if (!_initialized)
            {
                _operationCache.Enqueue(() => { OnSceneUnloaded(scene); });
                return;
            }

            if (!_enabled)
                return;

            ReportSceneUnload(scene);
        }

        /// <summary>
        /// Super Properties, refer to properties that would be uploaded by each event
        /// </summary>
        /// <param name="properties">super properties for events</param>
        public static void SetSuperProperties(Dictionary<string, object> properties)
        {
            if (!_initialized)
            {
                _operationCache.Enqueue(() => { SetSuperProperties(properties); });
                return;
            }

            if (!_enabled)
                return;

            PropertiesChecker.CheckProperties(properties);
            AnalyticsService.SetSuperProperties(properties);
        }

        /// <summary>
        /// Delete Property form current Super Properties
        /// </summary>
        /// <param name="property">property name</param>
        public static void UnsetSuperProperty(string property)
        {
            if (!_initialized)
            {
                _operationCache.Enqueue(() => { UnsetSuperProperty(property); });
                return;
            }

            if (!_enabled)
                return;

            AnalyticsService.UnsetSuperProperty(property);
        }

        /// <summary>
        /// Clear current Super Properties
        /// </summary>
        public static void ClearSuperProperties()
        {
            if (!_initialized)
            {
                _operationCache.Enqueue(ClearSuperProperties);
                return;
            }

            if (!_enabled)
                return;

            AnalyticsService.ClearSuperProperties();
        }

        private static void ReportSceneLoad(Scene scene)
        {
            Dictionary<string, object> properties = new Dictionary<string, object>()
            {
                { "#scene_name", scene.name },
                { "#scene_path", scene.path }
            };

            Dictionary<string, object> finalProperties = new Dictionary<string, object>(properties);
            CommonUtil.AddDictionary(finalProperties,
                AnalyticsService.GetEventHandler(AutoReportEventType.AppSceneLoad, properties));
            AnalyticsService.Report("scene_loaded", finalProperties);
            AnalyticsService.TimeEvent("scene_unloaded");
        }

        private static void ReportSceneUnload(Scene scene)
        {
            Dictionary<string, object> properties = new Dictionary<string, object>()
            {
                { "#scene_name", scene.name },
                { "#scene_path", scene.path }
            };

            Dictionary<string, object> finalProperties = new Dictionary<string, object>(properties);
            CommonUtil.AddDictionary(finalProperties,
                AnalyticsService.GetEventHandler(AutoReportEventType.AppSceneUnload, properties));
            if ((AnalyticsService.GetEventType() & AutoReportEventType.AppSceneUnload) != 0)
            {
                AnalyticsService.Report("scene_unloaded", finalProperties);
            }
        }

        public static async Task GetDeviceAdvertisingId()
        {
            var deviceAdvertiseingId = string.Empty;
#if UNITY_ANDROID && !UNITY_EDITOR
            deviceAdvertiseingId = await Unity.UOS.Insight.Wrapper.BaseWrapper.FetchOAIDAsync();
#elif UNITY_IOS && !UNITY_EDITOR
            deviceAdvertiseingId = await Unity.UOS.Insight.Wrapper.BaseWrapper.GetIDFA();
#elif UNITY_OPENHARMONY && !UNITY_EDITOR
            deviceAdvertiseingId = await Unity.UOS.Insight.Wrapper.BaseWrapper.GetOAIDOpenHarmony();
#else
            await Task.CompletedTask;
#endif
            AnalyticsService.SetDeviceAdId(deviceAdvertiseingId);
        }

        private static void FlushEventCaches()
        {
            while (_operationCache.TryDequeue(out var action))
            {
                action();
            }
        }
    }

    [Serializable]
    public class UosAppInfo
    {
        [Serializable]
        public class AppServiceConfig
        {
            public string ServiceName;
            public string Provider;
            public string Status;
            public Dictionary<string, string> Properties;
        }

        public Dictionary<string, AppServiceConfig> Services;
    }

    /// <summary>
    /// SDK Configuration information class
    /// </summary>
    [Serializable]
    public class DataAnalyticsConfig
    {
        /// <summary>
        /// Project ID
        /// </summary>
        public string appId;

        /// <summary>
        /// TIme Zone Type
        /// </summary>
        public AnalyticsTimeZone timeZone;

        /// <summary>
        /// Time Zone ID
        /// </summary>
        public string timeZoneId;

        /// <summary>
        /// enable data encryption, default is false (iOS/Android only)
        /// </summary>
        internal bool enableEncrypt;

        /// <summary>
        /// secret key version (iOS/Android only)
        /// </summary>
        internal int encryptVersion;

        /// <summary>
        /// public secret key (iOS/Android only)
        /// </summary>
        internal string encryptPublicKey;

        internal string symType;
        internal string asymType;

        /// <summary>
        /// SSL Pinning mode, default is NONE (iOS/Android only)
        /// </summary>
        public SSLPinningMode pinningMode;

        /// <summary>
        /// allow invalid certificates, default is false (iOS/Android only)
        /// </summary>
        public bool allowInvalidCertificates;

        /// <summary>
        /// enable to verify domain name, default is true (iOS/Android only)
        /// </summary>
        public bool validatesDomainName;

        /// <summary>
        /// SDK instance name, to call SDK quickly
        /// </summary>
        public string name
        {
            set
            {
                if (!string.IsNullOrEmpty(value)) sName = value.Replace(" ", "");
            }
            get { return sName; }
        } // instances name

        private string sName;

        /// <summary>
        /// Construct DataAnalyticsConfig instance
        /// </summary>
        /// <param name="appId"> project ID</param>
        public DataAnalyticsConfig(string appId)
        {
            this.appId = appId.Replace(" ", "");
            timeZone = AnalyticsTimeZone.Local;
            timeZoneId = null;
            enableEncrypt = false;
            encryptPublicKey = null;
            encryptVersion = 0;
            symType = null;
            asymType = null;
            pinningMode = SSLPinningMode.NONE;
            allowInvalidCertificates = false;
            validatesDomainName = true;
        }

        public void EnableEncrypt(string publicKey, int version = 0, string symType = "", string asymType = "")
        {
            enableEncrypt = true;
            encryptVersion = version;
            encryptPublicKey = publicKey;
            this.symType = symType;
            this.asymType = asymType;
        }

        /// <summary>
        /// Get Time Zone Identify Code in SDK
        /// </summary>
        /// <returns> Time Zone ID </returns>
        public string getTimeZoneId()
        {
            switch (timeZone)
            {
                case AnalyticsTimeZone.UTC:
                    return "UTC";
                case AnalyticsTimeZone.Asia_Shanghai:
                    return "Asia/Shanghai";
                case AnalyticsTimeZone.Asia_Tokyo:
                    return "Asia/Tokyo";
                case AnalyticsTimeZone.America_Los_Angeles:
                    return "America/Los_Angeles";
                case AnalyticsTimeZone.America_New_York:
                    return "America/New_York";
                case AnalyticsTimeZone.Other:
                    return timeZoneId;
                default:
                    break;
            }

            return null;
        }
    }
}
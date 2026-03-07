using System;
using System.Collections.Generic;
using Unity.UOS.Insight.Config;
using Unity.UOS.Insight.Models;
using Unity.UOS.Insight.Utils;
using UnityEngine;

namespace Unity.UOS.Insight.Service
{
    public static class AnalyticsService
    {
        private static readonly Dictionary<string, AnalyticsServiceInstance> Instances =
            new Dictionary<string, AnalyticsServiceInstance>();

        private static string CurrentAppid;

        private static AnalyticsServiceInstance GetInstance(string appId)
        {
            AnalyticsServiceInstance instance = null;
            if (!string.IsNullOrEmpty(appId))
            {
                appId = appId.Replace(" ", "");

                if (Instances.ContainsKey(appId))
                {
                    instance = Instances[appId];
                }
            }

            if (instance == null)
            {
                instance = Instances[CurrentAppid];
            }

            return instance;
        }

        public static AnalyticsServiceInstance CurrentInstance()
        {
            AnalyticsServiceInstance instance = Instances[CurrentAppid];
            return instance;
        }

        public static AnalyticsServiceInstance Init(string appId, string instanceName, InsightConfig config = null,
            MonoBehaviour mono = null)
        {
            if (CommonUtil.IsEmptyString(appId))
            {
                if (Log.GetEnable()) Log.w("appId is empty");
                return null;
            }

            AnalyticsServiceInstance instance = null;
            if (!string.IsNullOrEmpty(instanceName))
            {
                if (Instances.ContainsKey(instanceName))
                {
                    instance = Instances[instanceName];
                }
                else
                {
                    instance = new AnalyticsServiceInstance(appId, instanceName, config, mono);
                    if (string.IsNullOrEmpty(CurrentAppid))
                    {
                        CurrentAppid = instanceName;
                    }

                    Instances[instanceName] = instance;
                }
            }
            else
            {
                if (Instances.ContainsKey(appId))
                {
                    instance = Instances[appId];
                }
                else
                {
                    instance = new AnalyticsServiceInstance(appId, null, config, mono);
                    if (string.IsNullOrEmpty(CurrentAppid))
                    {
                        CurrentAppid = appId;
                    }

                    Instances[appId] = instance;
                }
            }

            return instance;
        }

        /// <summary>
        /// Sets distinct ID
        /// </summary>
        /// <param name="distinctID"></param>
        /// <param name="appId"></param>
        public static void Identifiy(string distinctID, string appId = "")
        {
            GetInstance(appId).Identifiy(distinctID);
        }

        /// <summary>
        /// Gets distinct ID
        /// </summary>
        /// <param name="appId"></param>
        /// <returns></returns>
        public static string DistinctId(string appId = "")
        {
            return GetInstance(appId).DistinctId();
        }

        /// <summary>
        /// Sets account ID
        /// </summary>
        /// <param name="accountID"></param>
        /// <param name="appId"></param>
        public static void Login(string accountID, string appId = "")
        {
            GetInstance(appId).Login(accountID);
        }

        /// <summary>
        /// Gets account ID
        /// </summary>
        /// <param name="appId"></param>
        /// <returns></returns>
        public static string AccountID(string appId = "")
        {
            return GetInstance(appId).AccountID();
        }

        /// <summary>
        /// Clear account ID
        /// </summary>
        public static void Logout(string appId = "")
        {
            GetInstance(appId).Logout();
        }

        /// <summary>
        /// Enable Auto-reporting Events
        /// </summary>
        /// <param name="events"></param>
        /// <param name="appId"></param>
        public static void EnableAutoReport(AutoReportEventType events, Dictionary<string, object> properties,
            string appId = "")
        {
            GetInstance(appId).EnableAutoReport(events, properties);
        }

        public static void EnableAutoReport(AutoReportEventType events, AutoReportEventHandler eventCallback,
            string appId = "")
        {
            GetInstance(appId).EnableAutoReport(events, eventCallback);
        }

        public static void SetAutoReportProperties(AutoReportEventType events, Dictionary<string, object> properties,
            string appId = "")
        {
            GetInstance(appId).SetAutoReportProperties(events, properties);
        }

        public static Dictionary<string, object> GetEventHandler(AutoReportEventType events, Dictionary<string, object> properties,
            string appId = "")
        {
            return GetInstance(appId).GetEventHandler(events, properties);
        }

        public static AutoReportEventType GetEventType(string appId = "")
        {
            return GetInstance(appId).GetEventType();
        }

        public static void Report(string eventName, string appId = "")
        {
            GetInstance(appId).Report(eventName);
        }

        public static void Report(string eventName, Dictionary<string, object> properties, string appId = "")
        {
            GetInstance(appId).Report(eventName, properties);
        }

        public static void Report(string eventName, Dictionary<string, object> properties, DateTime date,
            string appId = "")
        {
            GetInstance(appId).Report(eventName, properties, date);
        }

        public static void Report(string eventName, Dictionary<string, object> properties, DateTime date,
            TimeZoneInfo timeZone, string appId = "")
        {
            GetInstance(appId).Report(eventName, properties, date, timeZone);
        }

        public static void ReportForAll(string eventName, Dictionary<string, object> properties)
        {
            foreach (string appId in Instances.Keys)
            {
                GetInstance(appId).Report(eventName, properties);
            }
        }

        public static void Report(EventData eventModel, string appId = "")
        {
            GetInstance(appId).Report(eventModel);
        }

        public static void Flush(string appId = "")
        {
            GetInstance(appId).Flush();
        }

        //public static void FlushImmediately (string appId = "")
        //{
        //    GetInstance(appId).FlushImmediately();
        //}
        public static void SetSuperProperties(Dictionary<string, object> superProperties, string appId = "")
        {
            GetInstance(appId).SetSuperProperties(superProperties);
        }

        public static void UnsetSuperProperty(string propertyKey, string appId = "")
        {
            GetInstance(appId).UnsetSuperProperty(propertyKey);
        }

        public static Dictionary<string, object> SuperProperties(string appId = "")
        {
            return GetInstance(appId).SuperProperties();
        }

        public static Dictionary<string, object> PresetProperties(string appId = "")
        {
            return GetInstance(appId).PresetProperties();
        }

        public static void ClearSuperProperties(string appId = "")
        {
            GetInstance(appId).ClearSuperProperties();
        }

        public static void TimeEvent(string eventName, string appId = "")
        {
            GetInstance(appId).TimeEvent(eventName);
        }

        public static void TimeEventForAll(string eventName)
        {
            foreach (string appId in Instances.Keys)
            {
                GetInstance(appId).TimeEvent(eventName);
            }
        }

        /// <summary>
        /// Pause Event timing
        /// </summary>
        /// <param name="status">ture: puase timing, false: resume timing</param>
        /// <param name="eventName">event name (null or empty is for all event)</param>
        public static void PauseTimeEvent(bool status, string eventName = "", string appId = "")
        {
            GetInstance(appId).PauseTimeEvent(status, eventName);
        }

        public static void SetDynamicSuperProperties(DynamicSuperPropertiesHandler dynamicSuperProperties,
            string appId = "")
        {
            GetInstance(appId).SetDynamicSuperProperties(dynamicSuperProperties);
        }

        public static void SetReportStatus(ReportStatus status, string appId = "")
        {
            GetInstance(appId).SetReportStatus(status);
        }

        public static void OptReporting(bool optReporting, string appId = "")
        {
            GetInstance(appId).OptReporting(optReporting);
        }

        public static void EnableReporting(bool isEnable, string appId = "")
        {
            GetInstance(appId).EnableReporting(isEnable);
        }

        public static void CalibrateTime(long timestamp)
        {
            TimestampCalibration timestampCalibration = new TimestampCalibration(timestamp);
            AnalyticsServiceInstance.SetTimeCalibratieton(timestampCalibration);
        }

        public static void CalibrateTimeWithNtp(string ntpServer)
        {
            NTPCalibration ntpCalibration = new NTPCalibration(ntpServer);
            AnalyticsServiceInstance.SetNtpTimeCalibratieton(ntpCalibration);
        }

        public static void OnDestroy()
        {
            Instances.Clear();
        }

        public static string GetDeviceId()
        {
            return DeviceInfo.DeviceID();
        }

        public static string TimeString(DateTime dateTime, string appId = "")
        {
            return GetInstance(appId).TimeString(dateTime);
        }

        public static void SetDeviceAdId(string deviceAdId)
        {
            CurrentInstance().SetDeviceAdId(deviceAdId);
        }
    }
}

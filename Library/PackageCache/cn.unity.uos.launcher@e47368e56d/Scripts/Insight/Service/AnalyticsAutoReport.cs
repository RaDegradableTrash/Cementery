using System.Collections.Generic;
using Unity.UOS.Insight.Models;
using Unity.UOS.Insight.Utils;
using UnityEngine;

namespace Unity.UOS.Insight.Service
{
    public class AnalyticsAutoReport : MonoBehaviour
    {
        private string mAppId;
        private AutoReportEventType mAutoReportEvents = AutoReportEventType.None;
        private bool mStarted = false;

        private AutoReportEventHandler mEventCallback;
        private Dictionary<string, Dictionary<string, object>> mAutoReportProperties = new Dictionary<string, Dictionary<string, object>>();

        private const string InsightAutoReportEventType_APP_START = "AppStart";
        private const string InsightAutoReportEventType_APP_END = "AppEnd";
        private const string InsightAutoReportEventType_APP_CRASH = "AppCrash";
        private const string InsightAutoReportEventType_APP_INSTALL = "AppInstall";

        // Start is called before the first frame update
        void Start()
        {
        }
        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                if ((mAutoReportEvents & AutoReportEventType.AppStart) != 0)
                {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    if (mAutoReportProperties.ContainsKey(InsightAutoReportEventType_APP_START))
                    {
                        CommonUtil.AddDictionary(properties, mAutoReportProperties[InsightAutoReportEventType_APP_START]);
                    }
                    if (mEventCallback != null)
                    {
                        CommonUtil.AddDictionary(properties, mEventCallback.GetAutoReportEventProperties((int)AutoReportEventType.AppStart, properties));
                    }
                    AnalyticsService.Report(AnalyticsConstant.START_EVENT, properties, this.mAppId);
                }
                if ((mAutoReportEvents & AutoReportEventType.AppEnd) != 0)
                {
                    AnalyticsService.TimeEvent(AnalyticsConstant.END_EVENT, this.mAppId);
                }

                AnalyticsService.PauseTimeEvent(false, appId: this.mAppId);
            }
            else
            {
                if ((mAutoReportEvents & AutoReportEventType.AppEnd) != 0)
                {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    if (mAutoReportProperties.ContainsKey(InsightAutoReportEventType_APP_END))
                    {
                        CommonUtil.AddDictionary(properties, mAutoReportProperties[InsightAutoReportEventType_APP_END]);
                    }
                    if (mEventCallback != null)
                    {
                        CommonUtil.AddDictionary(properties, mEventCallback.GetAutoReportEventProperties((int)AutoReportEventType.AppEnd, properties));
                    }
                    AnalyticsService.Report(AnalyticsConstant.END_EVENT, properties, this.mAppId);
                }
                AnalyticsService.Flush(this.mAppId);

                AnalyticsService.PauseTimeEvent(true, appId: this.mAppId);
            }
        }

        void OnApplicationQuit()
        {
            if (Application.isFocused == true)
            {
                OnApplicationFocus(false);
            }
            //AnalyticsService.FlushImmediately(this.mAppId);
        }

        public void SetAppId(string appId)
        {
            this.mAppId = appId;
        }

        public void EnableAutoReport(AutoReportEventType events, Dictionary<string, object> properties, string appId)
        {
            mAutoReportEvents = events;
            SetAutoReportProperties(events, properties);
            if ((events & AutoReportEventType.AppInstall) != 0)
            {
                object result = File.GetData(appId, AnalyticsConstant.IS_INSTALL, typeof(int));
                if (result == null)
                {
                    Dictionary<string, object> mProperties = new Dictionary<string, object>(properties);
                    File.SaveData(appId, AnalyticsConstant.IS_INSTALL, 1);
                    if (mAutoReportProperties.ContainsKey(InsightAutoReportEventType_APP_INSTALL))
                    {
                        CommonUtil.AddDictionary(mProperties, mAutoReportProperties[InsightAutoReportEventType_APP_INSTALL]);
                    }
                    AnalyticsService.Report(AnalyticsConstant.INSTALL_EVENT, mProperties, this.mAppId);
                    AnalyticsService.Flush(this.mAppId);
                }
            }
            if ((events & AutoReportEventType.AppStart) != 0 && mStarted == false)
            {
                Dictionary<string, object> mProperties = new Dictionary<string, object>(properties);
                if (mAutoReportProperties.ContainsKey(InsightAutoReportEventType_APP_START))
                {
                    CommonUtil.AddDictionary(mProperties, mAutoReportProperties[InsightAutoReportEventType_APP_START]);
                }
                AnalyticsService.Report(AnalyticsConstant.START_EVENT, mProperties, this.mAppId);
                AnalyticsService.Flush(this.mAppId);
            }
            if ((events & AutoReportEventType.AppEnd) != 0 && mStarted == false)
            {
                AnalyticsService.TimeEvent(AnalyticsConstant.END_EVENT, this.mAppId);
            }

            mStarted = true;
        }

        public void EnableAutoReport(AutoReportEventType events, AutoReportEventHandler eventCallback, string appId)
        {
            mAutoReportEvents = events;
            mEventCallback = eventCallback;
            if ((events & AutoReportEventType.AppInstall) != 0)
            {
                object result = File.GetData(appId, AnalyticsConstant.IS_INSTALL, typeof(int));
                if (result == null)
                {
                    File.SaveData(appId, AnalyticsConstant.IS_INSTALL, 1);
                    Dictionary<string, object> properties = null;
                    if (mAutoReportProperties.ContainsKey(InsightAutoReportEventType_APP_INSTALL))
                    {
                        properties = mAutoReportProperties[InsightAutoReportEventType_APP_INSTALL];
                    }
                    else
                    {
                        properties = new Dictionary<string, object>();
                    }
                    if (mEventCallback != null)
                    {
                        CommonUtil.AddDictionary(properties, mEventCallback.GetAutoReportEventProperties((int)AutoReportEventType.AppInstall, properties));
                    }
                    AnalyticsService.Report(AnalyticsConstant.INSTALL_EVENT, properties, this.mAppId);
                    AnalyticsService.Flush(this.mAppId);
                }
            }
            if ((events & AutoReportEventType.AppStart) != 0 && mStarted == false)
            {
                Dictionary<string, object> properties = null;
                if (mAutoReportProperties.ContainsKey(InsightAutoReportEventType_APP_START))
                {
                    properties = mAutoReportProperties[InsightAutoReportEventType_APP_START];
                }
                else
                {
                    properties = new Dictionary<string, object>();
                }
                if (mEventCallback != null)
                {
                    CommonUtil.AddDictionary(properties, mEventCallback.GetAutoReportEventProperties((int)AutoReportEventType.AppStart, properties));
                }
                AnalyticsService.Report(AnalyticsConstant.START_EVENT, properties, this.mAppId);
                AnalyticsService.Flush(this.mAppId);
            }
            if ((events & AutoReportEventType.AppEnd) != 0 && mStarted == false)
            {
                AnalyticsService.TimeEvent(AnalyticsConstant.END_EVENT, this.mAppId);
            }

            mStarted = true;
        }

        public void SetAutoReportProperties(AutoReportEventType events, Dictionary<string, object> properties)
        {
            mAutoReportEvents = events;
            if ((events & AutoReportEventType.AppInstall) != 0)
            {
                if (mAutoReportProperties.ContainsKey(InsightAutoReportEventType_APP_INSTALL))
                {
                    CommonUtil.AddDictionary(mAutoReportProperties[InsightAutoReportEventType_APP_INSTALL], properties);
                }
                else
                    mAutoReportProperties[InsightAutoReportEventType_APP_INSTALL] = properties;
            }
            if ((events & AutoReportEventType.AppStart) != 0)
            {
                if (mAutoReportProperties.ContainsKey(InsightAutoReportEventType_APP_START))
                {
                    CommonUtil.AddDictionary(mAutoReportProperties[InsightAutoReportEventType_APP_START], properties);
                }
                else
                    mAutoReportProperties[InsightAutoReportEventType_APP_START] = properties;
            }
            if ((events & AutoReportEventType.AppEnd) != 0)
            {
                if (mAutoReportProperties.ContainsKey(InsightAutoReportEventType_APP_END))
                {
                    CommonUtil.AddDictionary(mAutoReportProperties[InsightAutoReportEventType_APP_END], properties);
                }
                else
                    mAutoReportProperties[InsightAutoReportEventType_APP_END] = properties;
            }
            if ((events & AutoReportEventType.AppCrash) != 0)
            {
                if (mAutoReportProperties.ContainsKey(InsightAutoReportEventType_APP_CRASH))
                {
                    CommonUtil.AddDictionary(mAutoReportProperties[InsightAutoReportEventType_APP_CRASH], properties);
                }
                else
                    mAutoReportProperties[InsightAutoReportEventType_APP_CRASH] = properties;
            }
        }

        public Dictionary<string, object> GetEventHandler(AutoReportEventType events, Dictionary<string, object> properties)
        {
            return this.mEventCallback.GetAutoReportEventProperties((int)events, properties);
        }

        public AutoReportEventType GetEventType()
        {
            return this.mAutoReportEvents;
        }
    }
}
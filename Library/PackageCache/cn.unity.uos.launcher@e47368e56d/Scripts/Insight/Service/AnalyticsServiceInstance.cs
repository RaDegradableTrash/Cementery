using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.UOS.Insight.Config;
using Unity.UOS.Insight.Models;
using Unity.UOS.Insight.Service.Request;
using Unity.UOS.Insight.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.UOS.Insight.Service
{
    public class AnalyticsServiceInstance
    {
        private string mAppid;
        protected string mDistinctID;
        protected string mAccountID;
        private bool mOptReporting = true;
        private Dictionary<string, object> mTimeEvents = new Dictionary<string, object>();
        private Dictionary<string, object> mTimeEventsBefore = new Dictionary<string, object>();
        private bool mEnableReporting = true;
        private bool mEventSaveOnly = false; //data stores in the cache, but not be reported
        protected Dictionary<string, object> mSupperProperties = new Dictionary<string, object>();

        protected Dictionary<string, Dictionary<string, object>> mAutoReportProperties = new Dictionary<string, Dictionary<string, object>>();

        private InsightConfig mConfig;
        private AnalyticsRequest mRequest;
        private static TimeCalibration mTimeCalibration;
        private static TimeCalibration mNtpTimeCalibration;
        private DynamicSuperPropertiesHandler mDynamicSuperProperties;
        private string deviceAdId;

        private Task mTask
        {
            get { return Task.SingleTask(); }
            set { this.mTask = value; }
        }

        private static AnalyticsServiceInstance mCurrentInstance;
        private MonoBehaviour mMono;
        private static MonoBehaviour sMono;
        private AnalyticsAutoReport mAutoReport;

        WaitForSeconds flushDelay;

        public static void SetTimeCalibratieton(TimeCalibration timeCalibration)
        {
            mTimeCalibration = timeCalibration;
        }

        public static void SetNtpTimeCalibratieton(TimeCalibration timeCalibration)
        {
            mNtpTimeCalibration = timeCalibration;
        }

        private AnalyticsServiceInstance()
        {
        }

        private void DefaultData()
        {
            DistinctId();
            AccountID();
            SuperProperties();
            DefaultReportState();
        }

        public AnalyticsServiceInstance(string appId, string server) : this(appId, server, null, null)
        {
        }

        public AnalyticsServiceInstance(string appId, string instanceName, InsightConfig config,
            MonoBehaviour mono = null)
        {
            this.mMono = mono;
            sMono = mono;
            if (config == null)
            {
                this.mConfig = InsightConfig.GetInstance(appId, instanceName);
            }
            else
            {
                this.mConfig = config;
            }

            flushDelay = new WaitForSeconds(mConfig.mUploadInterval);
            sMono.StartCoroutine(WaitAndFlush());

            this.mAppid = appId;

            mRequest = new AnalyticsRequest();

            DefaultData();
            mCurrentInstance = this;
            // dynamic loading Task AnalyticsAutoReport
            GameObject mInsightSDKTask = new GameObject("InsightSDKTask", typeof(Task));
            UnityEngine.Object.DontDestroyOnLoad(mInsightSDKTask);

            GameObject mInsightSDKAutoReport = new GameObject("AnalyticsAutoReport", typeof(AnalyticsAutoReport));
            this.mAutoReport = (AnalyticsAutoReport)mInsightSDKAutoReport.GetComponent(typeof(AnalyticsAutoReport));
            if (!string.IsNullOrEmpty(instanceName))
            {
                this.mAutoReport.SetAppId(instanceName);
            }
            else
            {
                this.mAutoReport.SetAppId(this.mAppid);
            }

            UnityEngine.Object.DontDestroyOnLoad(mInsightSDKAutoReport);
        }

        private void EventResponseHandle(Dictionary<string, object> result)
        {
            int eventCount = 0;
            if (result != null)
            {
                int flushCount = 0;
                if (result.ContainsKey("flush_count"))
                {
                    flushCount = (int)result["flush_count"];
                }

                if (!string.IsNullOrEmpty(this.mConfig.InstanceName()))
                {
                    eventCount = FileJson.DeleteBatchReportingData(flushCount, this.mConfig.InstanceName());
                }
                else
                {
                    eventCount = FileJson.DeleteBatchReportingData(flushCount, this.mAppid);
                }
            }

            mTask.Release();
            if (eventCount > 0)
            {
                if (Log.GetEnable()) Log.i("Flush automatically (" + this.mAppid + ")");
                Flush();
            }
        }

        public TimeInter GetTime(DateTime dateTime)
        {
            TimeInter time = null;

            if (dateTime == DateTime.MinValue || dateTime == null)
            {
                if (mNtpTimeCalibration != null) // check if time calibrated
                {
                    time = new CalibratedTime(mNtpTimeCalibration, mConfig.TimeZone());
                }
                else if (mTimeCalibration != null) // check if time calibrated
                {
                    time = new CalibratedTime(mTimeCalibration, mConfig.TimeZone());
                }
                else
                {
                    time = new InsightTime(mConfig.TimeZone(), DateTime.Now);
                }
            }
            else
            {
                time = new InsightTime(mConfig.TimeZone(), dateTime);
            }

            return time;
        }

        // sets distisct ID
        public virtual void Identifiy(string distinctID)
        {
            if (Log.GetEnable()) Log.i("Setting distinct ID, DistinctId = " + distinctID);
            if (IsPaused())
            {
                return;
            }

            if (!string.IsNullOrEmpty(distinctID))
            {
                this.mDistinctID = distinctID;
                File.SaveData(mAppid, AnalyticsConstant.DISTINCT_ID, distinctID);
            }
        }

        public virtual string DistinctId()
        {
            this.mDistinctID = (string)File.GetData(this.mAppid, AnalyticsConstant.DISTINCT_ID, typeof(string));
            if (string.IsNullOrEmpty(this.mDistinctID))
            {
                this.mDistinctID = CommonUtil.RandomID();
                File.SaveData(this.mAppid, AnalyticsConstant.DISTINCT_ID, this.mDistinctID);
            }

            return this.mDistinctID;
        }

        public virtual void Login(string accountID)
        {
            if (IsPaused())
            {
                return;
            }

            if (Log.GetEnable()) Log.i("Login SDK, AccountId = " + accountID);
            if (!string.IsNullOrEmpty(accountID))
            {
                this.mAccountID = accountID;
                File.SaveData(mAppid, AnalyticsConstant.ACCOUNT_ID, accountID);
            }
        }

        public virtual string AccountID()
        {
            this.mAccountID = (string)File.GetData(this.mAppid, AnalyticsConstant.ACCOUNT_ID, typeof(string));
            return this.mAccountID;
        }

        public virtual void Logout()
        {
            if (IsPaused())
            {
                return;
            }

            if (Log.GetEnable()) Log.i("Logout SDK");
            this.mAccountID = "";
            File.DeleteData(this.mAppid, AnalyticsConstant.ACCOUNT_ID);
        }

        //TODO
        public virtual void EnableAutoReport(AutoReportEventType events, Dictionary<string, object> properties)
        {
            this.mAutoReport.EnableAutoReport(events, properties, mAppid);
        }

        public virtual void EnableAutoReport(AutoReportEventType events, AutoReportEventHandler eventCallback)
        {
            this.mAutoReport.EnableAutoReport(events, eventCallback, mAppid);
        }

        // sets auto-Reporting events properties
        public virtual void SetAutoReportProperties(AutoReportEventType events, Dictionary<string, object> properties)
        {
            this.mAutoReport.SetAutoReportProperties(events, properties);
        }

        public virtual Dictionary<string, object> GetEventHandler(AutoReportEventType events, Dictionary<string, object> properties)
        {
            return this.mAutoReport.GetEventHandler(events, properties);
        }

        public virtual AutoReportEventType GetEventType()
        {
            return this.mAutoReport.GetEventType();
        }

        public void Report(string eventName)
        {
            Report(eventName, null, DateTime.MinValue);
        }

        public void Report(string eventName, Dictionary<string, object> properties)
        {
            Report(eventName, getFinalEventProperties(properties), DateTime.MinValue);
        }

        private Dictionary<string, object> getFinalEventProperties(Dictionary<string, object> properties)
        {
            PropertiesChecker.CheckProperties(properties);

            if (null != mDynamicSuperProperties)
            {
                Dictionary<string, object> finalProperties = new Dictionary<string, object>();
                PropertiesChecker.MergeProperties(mDynamicSuperProperties.GetDynamicSuperProperties(), finalProperties);
                PropertiesChecker.MergeProperties(properties, finalProperties);
                return finalProperties;
            }
            else
            {
                return properties;
            }
        }

        public void Report(string eventName, Dictionary<string, object> properties, DateTime date)
        {
            Report(eventName, properties, date, null, false);
        }

        public void Report(string eventName, Dictionary<string, object> properties, DateTime date,
            TimeZoneInfo timeZone)
        {
            Report(eventName, properties, date, timeZone, false);
        }

        public void Report(string eventName, Dictionary<string, object> properties, DateTime date,
            TimeZoneInfo timeZone, bool immediately)
        {

            PropertiesChecker.CheckString(eventName);
            TimeInter time = GetTime(date);
            EventData data = new EventData(time, eventName, properties);
            if (timeZone != null)
            {
                data.SetTimeZone(timeZone);
            }

            SendData(data, immediately);
        }

        private void SendData(EventData data)
        {
            SendData(data, false);
        }

        private void SendData(EventData data, bool immediately)
        {
            if (this.mDynamicSuperProperties != null)
            {
                data.SetProperties(this.mDynamicSuperProperties.GetDynamicSuperProperties(), false);
            }

            if (this.mSupperProperties != null && this.mSupperProperties.Count > 0)
            {
                data.SetProperties(this.mSupperProperties, false);
            }

            Dictionary<string, object> deviceInfo = CommonUtil.GetDeviceInfo();
            if (!string.IsNullOrEmpty(this.deviceAdId))
            {
                deviceInfo[AnalyticsConstant.DEVICE_AD_ID] = this.deviceAdId;
            }

            data.SetProperties(deviceInfo, false);

            float duration = 0;
            if (mTimeEvents.ContainsKey(data.EventName()))
            {
                int beginTime = (int)mTimeEvents[data.EventName()];
                int nowTime = Environment.TickCount;
                duration = (float)((nowTime - beginTime) / 1000.0);
                mTimeEvents.Remove(data.EventName());
                if (mTimeEventsBefore.ContainsKey(data.EventName()))
                {
                    int beforeTime = (int)mTimeEventsBefore[data.EventName()];
                    duration = duration + (float)(beforeTime / 1000.0);
                    mTimeEventsBefore.Remove(data.EventName());
                }
            }

            if (duration != 0)
            {
                data.SetDuration(duration);
            }

            SendData((BaseData)data, immediately);
        }

        private void SendData(BaseData data)
        {
            SendData(data, false);
        }

        private void SendData(BaseData data, bool immediately)
        {
            if (IsPaused())
            {
                return;
            }

            if (!string.IsNullOrEmpty(this.mAccountID))
            {
                data.SetAccountID(this.mAccountID);
            }

            if (string.IsNullOrEmpty(this.mDistinctID))
            {
                DistinctId();
            }

            data.SetDistinctID(this.mDistinctID);

            if (this.mConfig.IsDisabledEvent(data.EventName()))
            {
                if (Log.GetEnable()) Log.i("Disabled Event: " + data.EventName());
                return;
            }

            if (immediately)
            {
                Dictionary<string, object> dataDic = data.ToDictionary();
                this.mMono.StartCoroutine(AnalyticsRequest.SendData_2(null, MiniJson.Serialize(dataDic), 1));
            }
            else
            {
                Dictionary<string, object> dataDic = data.ToDictionary();
                int count = 0;
                if (!string.IsNullOrEmpty(this.mConfig.InstanceName()))
                {
                    if (Log.GetEnable())
                        Log.i("Enqueue data: \n" + MiniJson.Serialize(dataDic) + "\n  AppId: " + this.mAppid);
                    count = FileJson.EnqueueReportingData(dataDic, this.mConfig.InstanceName());
                }
                else
                {
                    if (Log.GetEnable())
                        Log.i("Enqueue data: \n" + MiniJson.Serialize(dataDic) + "\n  AppId: " + this.mAppid);
                    count = FileJson.EnqueueReportingData(dataDic, this.mAppid);
                }

                if (count >= this.mConfig.mUploadSize)
                {
                    if (Log.GetEnable()) Log.i("Flush automatically (" + this.mAppid + ")");
                    Flush();
                }
            }
        }

        private IEnumerator WaitAndFlush()
        {
            while (true)
            {
                yield return flushDelay;
                if (Log.GetEnable()) Log.i("Flush automatically (" + this.mAppid + ")");
                Flush();
            }
        }

        /// <summary>
        /// flush events data
        /// </summary>
        public virtual void Flush()
        {
            if (mEventSaveOnly == false)
            {
                mTask.SyncInvokeAllTask();

                int batchSize = mConfig.mUploadSize;
                if (!string.IsNullOrEmpty(this.mConfig.InstanceName()))
                {
                    mTask.StartRequest(mRequest, EventResponseHandle, batchSize, this.mConfig.InstanceName());
                }
                else
                {
                    mTask.StartRequest(mRequest, EventResponseHandle, batchSize, this.mAppid);
                }
            }
        }

        public void Report(EventData eventModel)
        {
            TimeInter time = GetTime(eventModel.Time());
            eventModel.SetTime(time);
            SendData(eventModel);
        }

        public virtual void SetSuperProperties(Dictionary<string, object> superProperties)
        {
            if (IsPaused())
            {
                return;
            }

            Dictionary<string, object> properties = new Dictionary<string, object>();
            string propertiesStr = (string)File.GetData(this.mAppid, AnalyticsConstant.SUPER_PROPERTY, typeof(string));
            if (!string.IsNullOrEmpty(propertiesStr))
            {
                properties = MiniJson.Deserialize(propertiesStr);
            }

            CommonUtil.AddDictionary(properties, superProperties);
            this.mSupperProperties = properties;
            File.SaveData(this.mAppid, AnalyticsConstant.SUPER_PROPERTY, MiniJson.Serialize(this.mSupperProperties));
        }

        public virtual void UnsetSuperProperty(string propertyKey)
        {
            if (IsPaused())
            {
                return;
            }

            PropertiesChecker.CheckString(propertyKey);
            Dictionary<string, object> properties = new Dictionary<string, object>();
            string propertiesStr = (string)File.GetData(this.mAppid, AnalyticsConstant.SUPER_PROPERTY, typeof(string));
            if (!string.IsNullOrEmpty(propertiesStr))
            {
                properties = MiniJson.Deserialize(propertiesStr);
            }

            if (properties.ContainsKey(propertyKey))
            {
                properties.Remove(propertyKey);
            }

            this.mSupperProperties = properties;
            File.SaveData(this.mAppid, AnalyticsConstant.SUPER_PROPERTY, MiniJson.Serialize(this.mSupperProperties));
        }

        public virtual Dictionary<string, object> SuperProperties()
        {
            string propertiesStr = (string)File.GetData(this.mAppid, AnalyticsConstant.SUPER_PROPERTY, typeof(string));
            if (!string.IsNullOrEmpty(propertiesStr))
            {
                this.mSupperProperties = MiniJson.Deserialize(propertiesStr);
            }

            return this.mSupperProperties;
        }

        public Dictionary<string, object> PresetProperties()
        {
            Dictionary<string, object> presetProperties = new Dictionary<string, object>();
            presetProperties[AnalyticsConstant.DEVICE_ID] = DeviceInfo.DeviceID();
            presetProperties[AnalyticsConstant.CARRIER] = DeviceInfo.Carrier();
            presetProperties[AnalyticsConstant.OS] = DeviceInfo.OS();
            presetProperties[AnalyticsConstant.SCREEN_HEIGHT] = DeviceInfo.ScreenHeight();
            presetProperties[AnalyticsConstant.SCREEN_WIDTH] = DeviceInfo.ScreenWidth();
            presetProperties[AnalyticsConstant.MANUFACTURE] = DeviceInfo.Manufacture();
            presetProperties[AnalyticsConstant.DEVICE_MODEL] = DeviceInfo.DeviceModel();
            presetProperties[AnalyticsConstant.SYSTEM_LANGUAGE] = DeviceInfo.MachineLanguage();
            presetProperties[AnalyticsConstant.OS_VERSION] = DeviceInfo.OSVersion();
            presetProperties[AnalyticsConstant.NETWORK_TYPE] = DeviceInfo.NetworkType();
            presetProperties[AnalyticsConstant.APP_BUNDLE_ID] = GeneralInfo.AppIdentifier();
            presetProperties[AnalyticsConstant.APP_VERSION] = GeneralInfo.AppVersion();
            presetProperties[AnalyticsConstant.ZONE_OFFSET] =
                CommonUtil.ZoneOffset(DateTime.Now, this.mConfig.TimeZone());

            return presetProperties;
        }

        public virtual void ClearSuperProperties()
        {
            if (IsPaused())
            {
                return;
            }

            this.mSupperProperties.Clear();
            File.DeleteData(this.mAppid, AnalyticsConstant.SUPER_PROPERTY);
        }

        public void TimeEvent(string eventName)
        {
            PropertiesChecker.CheckString(eventName);
            if (!mTimeEvents.ContainsKey(eventName))
            {
                mTimeEvents.Add(eventName, Environment.TickCount);
            }
        }

        /// <summary>
        /// Pause Event timing
        /// </summary>
        /// <param name="status">ture: puase timing, false: resume timing</param>
        /// <param name="eventName">event name (null or empty is for all event)</param>
        public void PauseTimeEvent(bool status, string eventName = "")
        {
            if (string.IsNullOrEmpty(eventName))
            {
                string[] eventNames = new string[mTimeEvents.Keys.Count];
                mTimeEvents.Keys.CopyTo(eventNames, 0);
                for (int i = 0; i < eventNames.Length; i++)
                {
                    string key = eventNames[i];
                    if (status == true)
                    {
                        int startTime = int.Parse(mTimeEvents[key].ToString());
                        int pauseTime = Environment.TickCount;
                        int duration = pauseTime - startTime;
                        if (mTimeEventsBefore.ContainsKey(key))
                        {
                            duration = duration + int.Parse(mTimeEventsBefore[key].ToString());
                        }

                        mTimeEventsBefore[key] = duration;
                    }
                    else
                    {
                        mTimeEvents[key] = Environment.TickCount;
                    }
                }
            }
            else
            {
                if (status == true)
                {
                    int startTime = int.Parse(mTimeEvents[eventName].ToString());
                    int pauseTime = Environment.TickCount;
                    int duration = pauseTime - startTime;
                    mTimeEventsBefore[eventName] = duration;
                }
                else
                {
                    mTimeEvents[eventName] = Environment.TickCount;
                }
            }
        }

        public void SetDynamicSuperProperties(DynamicSuperPropertiesHandler dynamicSuperProperties)
        {
            if (IsPaused())
            {
                return;
            }

            this.mDynamicSuperProperties = dynamicSuperProperties;
        }

        protected bool IsPaused()
        {
            bool mIsPaused = !mEnableReporting || !mOptReporting;
            if (mIsPaused)
            {
                if (Log.GetEnable()) Log.i("SDK Report status is Pause or Stop");
            }

            return mIsPaused;
        }

        public void SetReportStatus(ReportStatus status)
        {
            if (Log.GetEnable()) Log.i("Change Status to " + status);
            switch (status)
            {
                case ReportStatus.Pause:
                    mEventSaveOnly = false;
                    OptReporting(true);
                    EnableReporting(false);
                    break;
                case ReportStatus.Stop:
                    mEventSaveOnly = false;
                    EnableReporting(true);
                    OptReporting(false);
                    break;
                case ReportStatus.SaveOnly:
                    mEventSaveOnly = true;
                    EnableReporting(true);
                    OptReporting(true);
                    break;
                case ReportStatus.Normal:
                default:
                    mEventSaveOnly = false;
                    OptReporting(true);
                    EnableReporting(true);
                    Flush();
                    break;
            }
        }

        public void OptReporting(bool optReporting)
        {
            mOptReporting = optReporting;
            int opt = optReporting ? 1 : 0;
            File.SaveData(mAppid, AnalyticsConstant.OPT_REPORT, opt);
            if (!optReporting)
            {
                File.DeleteData(mAppid, AnalyticsConstant.ACCOUNT_ID);
                File.DeleteData(mAppid, AnalyticsConstant.DISTINCT_ID);
                File.DeleteData(mAppid, AnalyticsConstant.SUPER_PROPERTY);
                this.mAccountID = null;
                this.mDistinctID = null;
                this.mSupperProperties = new Dictionary<string, object>();
                FileJson.DeleteAllReportingData(mAppid);
            }
        }

        public void EnableReporting(bool isEnable)
        {
            mEnableReporting = isEnable;
            int enable = isEnable ? 1 : 0;
            File.SaveData(mAppid, AnalyticsConstant.ENABLE_REPORT, enable);
        }

        private void DefaultReportState()
        {
            object enableReport = File.GetData(mAppid, AnalyticsConstant.ENABLE_REPORT, typeof(int));
            object optReport = File.GetData(mAppid, AnalyticsConstant.OPT_REPORT, typeof(int));
            if (enableReport != null)
            {
                this.mEnableReporting = ((int)enableReport) == 1;
            }
            else
            {
                this.mEnableReporting = true;
            }

            if (optReport != null)
            {
                this.mOptReporting = ((int)optReport) == 1;
            }
            else
            {
                this.mOptReporting = true;
            }
        }

        public string TimeString(DateTime dateTime)
        {
            return CommonUtil.FormatDate(dateTime, mConfig.TimeZone());
        }

        public void SetDeviceAdId(string deviceAdId)
        {
            if (IsPaused())
            {
                return;
            }

            if (!string.IsNullOrEmpty(deviceAdId))
            {
                this.deviceAdId = deviceAdId;
            }
        }
    }
}
using System;
using System.Collections.Generic;
using Unity.UOS.Insight.Utils;

namespace Unity.UOS.Insight.Config
{
    public class InsightConfig
    {
        //private string mToken;
        //private string mNormalUrl;
        //private string mDebugUrl;
        //private string mConfigUrl;
        private string mInstanceName;
        //private Mode mMode = Mode.Normal;
        private TimeZoneInfo mTimeZone;
        public int mUploadInterval = 30;
        public int mUploadSize = 30;
        private List<string> mDisableEvents = new List<string>();
        private static Dictionary<string, InsightConfig> sInstances = new Dictionary<string, InsightConfig>();
        //private ResponseHandle mCallback;

        private InsightConfig(string token, string instanceName)
        {
            //this.mToken = token;
            this.mInstanceName = instanceName;
            try
            {
                this.mTimeZone = TimeZoneInfo.Local;
            }
            catch (Exception e)
            {
                if (Log.GetEnable()) Log.e("TimeZoneInfo initial failed :" + e.Message);
            }
        }

        private string VerifyUrl(string serverUrl)
        {
            /*Uri uri = new Uri(serverUrl);
            serverUrl = uri.Scheme + "://" + uri.Host + ":" + uri.Port;*/
            return serverUrl;
        }

        //public void SetMode(Mode mode)
        //{
        //    this.mMode = mode;
        //}

        //public Mode GetMode()
        //{
        //    return this.mMode;
        //}

        public string InstanceName()
        {
            return this.mInstanceName;
        }

        public static InsightConfig GetInstance(string token, string instanceName)
        {
            InsightConfig config = null;
            if (!string.IsNullOrEmpty(instanceName))
            {
                if (sInstances.ContainsKey(instanceName))
                {
                    config = sInstances[instanceName];
                }
                else
                {
                    config = new InsightConfig(token, instanceName);
                    sInstances.Add(instanceName, config);
                }
            }
            else
            {
                if (sInstances.ContainsKey(token))
                {
                    config = sInstances[token];
                }
                else
                {
                    config = new InsightConfig(token, null);
                    sInstances.Add(token, config);
                }
            }

            return config;
        }

        public void SetTimeZone(TimeZoneInfo timeZoneInfo)
        {
            this.mTimeZone = timeZoneInfo;
        }

        public TimeZoneInfo TimeZone()
        {
            return this.mTimeZone;
        }

        public List<string> DisableEvents()
        {
            return this.mDisableEvents;
        }

        public bool IsDisabledEvent(string eventName)
        {
            if (this.mDisableEvents == null)
            {
                return false;
            }
            else
            {
                return this.mDisableEvents.Contains(eventName);
            }
        }
    }
}
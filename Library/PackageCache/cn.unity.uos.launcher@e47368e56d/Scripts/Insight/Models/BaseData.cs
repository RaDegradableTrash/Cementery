using System;
using System.Collections.Generic;

namespace Unity.UOS.Insight.Models
{
    public abstract class BaseData
    {
        // event type
        private string mType;
        // event time
        private TimeInter mTime;
        // distinct ID
        private string mDistinctID;
        // event name
        private string mEventName;
        // account ID
        private string mAccountID;

        // unique ID for the event
        private string mUUID;
        private Dictionary<string, object> mProperties = new Dictionary<string, object>();
        public Dictionary<string, object> Properties()
        {
            return mProperties;
        }
        public void SetEventName(string eventName)
        {
            this.mEventName = eventName;
        }
        public void SetEventType(string eventType)
        {
            this.mType = eventType;
        }
        public string EventName()
        {
            return this.mEventName;
        }
        public void SetTime(TimeInter time)
        {
            this.mTime = time;
        }
        public TimeInter EventTime()
        {
            return this.mTime;
        }
        public void SetDataType(string type)
        {
            this.mType = type;
        }
        public virtual String GetDataType()
        {
            return this.mType;
        }
        public string AccountID()
        {
            return this.mAccountID;
        }
        public string DistinctID()
        {
            return this.mDistinctID;
        }
        public void SetAccountID(string accuntID)
        {
            this.mAccountID = accuntID;
        }
        public void SetDistinctID(string distinctID)
        {
            this.mDistinctID = distinctID;
        }
        public string UUID()
        {
            return this.mUUID;
        }
        public BaseData() { }
        public BaseData(TimeInter time, string eventName)
        {
            this.SetBaseData(eventName);
            this.SetTime(time);
        }
        public BaseData(string eventName)
        {
            this.SetBaseData(eventName);
        }
        public void SetBaseData(string eventName)
        {
            this.mEventName = eventName;
            this.mUUID = System.Guid.NewGuid().ToString();
        }

        public BaseData(TimeInter time, string eventName, Dictionary<string, object> properties) : this(time, eventName)
        {
            if (properties != null)
            {
                this.SetProperties(properties);
            }
        }

        public abstract Dictionary<string, object> ToDictionary();
        public void SetProperties(Dictionary<string, object> properties, bool isOverwrite = true)
        {
            if (isOverwrite)
            {
                foreach (KeyValuePair<string, object> kv in properties)
                {
                    mProperties[kv.Key] = kv.Value;

                }
            }
            else
            {
                foreach (KeyValuePair<string, object> kv in properties)
                {
                    if (!mProperties.ContainsKey(kv.Key))
                    {
                        mProperties[kv.Key] = kv.Value;
                    }

                }
            }

        }

    }
}
using System;
using System.Collections.Generic;
using Unity.UOS.Insight.Utils;

namespace Unity.UOS.Insight.Models
{
    public class EventData : BaseData
    {
        private DateTime mEventTime;
        private TimeZoneInfo mTimeZone;
        private float mDuration;
        private static Dictionary<string, object> mData;

        public void SetEventTime(DateTime dateTime)
        {
            mEventTime = dateTime;
        }

        public void SetTimeZone(TimeZoneInfo timeZone)
        {
            mTimeZone = timeZone;
        }

        public DateTime Time()
        {
            return mEventTime;
        }

        public EventData(string eventName) : base(eventName)
        {
        }

        public EventData(TimeInter time, string eventName) : base(time, eventName)
        {
        }

        public EventData(TimeInter time, string eventName, Dictionary<string, object> properties) : base(time,
            eventName, properties)
        {
        }

        public override string GetDataType()
        {
            return "track";
        }

        public void SetDuration(float duration)
        {
            mDuration = duration;
        }

        public override Dictionary<string, object> ToDictionary()
        {
            if (mData == null)
            {
                mData = new Dictionary<string, object>();
            }
            else
            {
                mData.Clear();
            }

            mData[AnalyticsConstant.USER_ID] = AccountID();
            mData[AnalyticsConstant.TIME] = EventTime().GetTime(mTimeZone);
            mData[AnalyticsConstant.EVENT_NAME] = EventName();

            mData[AnalyticsConstant.UUID] = UUID();
            var properties = Properties();
            properties[AnalyticsConstant.ZONE_OFFSET] = EventTime().GetZoneOffset(mTimeZone);
            if (mDuration != 0)
            {
                properties[AnalyticsConstant.DURATION] = mDuration;
            }

            mData[AnalyticsConstant.PROPERTIES] = properties;
            return mData;
        }
    }
}
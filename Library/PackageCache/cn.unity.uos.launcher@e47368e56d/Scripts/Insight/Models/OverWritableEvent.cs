using System.Collections.Generic;
using Unity.UOS.Insight.Utils;

namespace Unity.UOS.Insight.Models
{
    public class OverWritableEvent : EventData
    {
        private string mEventID;
        public OverWritableEvent(string eventName, string eventID) : base(eventName)
        {
            this.mEventID = eventID;
        }
        public override string GetDataType()
        {
            return "track_overwrite";
        }
        override public Dictionary<string, object> ToDictionary()
        {
            Dictionary<string, object> dictionary = base.ToDictionary();
            dictionary[AnalyticsConstant.EVENT_ID] = mEventID;
            return dictionary;
        }
    }
}
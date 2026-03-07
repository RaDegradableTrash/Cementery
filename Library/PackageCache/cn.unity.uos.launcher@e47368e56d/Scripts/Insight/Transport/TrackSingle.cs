using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.UOS.Insight.Transport
{
    public class TrackSingle
    {
        [JsonProperty(PropertyName = "user_id")]
        public string UserID { get; set; }

        [JsonProperty(PropertyName = "time")]
        public DateTime Time { get; set; }

        [JsonProperty(PropertyName = "event_name")]
        public string EventName { get; set; }

        [JsonProperty(PropertyName = "properties", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> Properties { get; set; }
    }
}
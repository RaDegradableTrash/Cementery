using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.UOS.Insight.Transport
{
    public class TrackRequest
    {
        [JsonProperty(PropertyName = "data")]
        public List<TrackSingle> Data { get; set; }
    }
}
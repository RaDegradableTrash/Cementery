using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.UOS.Insight.Transport
{
    public class TrackResponse
    {
        [JsonProperty(PropertyName = "failed")]
        private List<FailedInfo> FailedInfos { get; set; }
    }
}
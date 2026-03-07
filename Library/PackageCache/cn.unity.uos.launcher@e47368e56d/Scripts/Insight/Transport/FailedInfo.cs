using Newtonsoft.Json;

namespace Unity.UOS.Insight.Transport
{
    public class FailedInfo
    {
        [JsonProperty(PropertyName = "index")]
        public int Index { get; set; }

        [JsonProperty(PropertyName = "error")]
        public string Error { get; set; }
    }
}
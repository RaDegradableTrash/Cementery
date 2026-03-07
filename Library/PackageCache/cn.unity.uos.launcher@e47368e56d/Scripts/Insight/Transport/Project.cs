using Newtonsoft.Json;

namespace Unity.UOS.Insight.Transport
{
    public class Project
    {
        [JsonProperty(PropertyName = "batchSize")]
        public int BatchSize { get; set; }

        [JsonProperty(PropertyName = "syncInterval")]
        public int SyncInterval { get; set; }
    }
}
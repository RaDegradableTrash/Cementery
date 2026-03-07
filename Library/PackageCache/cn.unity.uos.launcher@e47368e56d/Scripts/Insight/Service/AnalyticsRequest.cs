using System.Collections;
using System.Collections.Generic;
using Unity.UOS.Insight.Transport;
using Unity.UOS.Insight.Utils;
using UnityEngine;

namespace Unity.UOS.Insight.Service.Request
{
    public class AnalyticsRequest
    {
        public static IEnumerator SendData_2(ResponseHandle responseHandle, string data, int eventCount)
        {
            var req = $"{{\"data\": {data}}}";
            var task = TransportClient.Track(req);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted && task.Exception != null)
            {
                foreach (var innerEx in task.Exception.InnerExceptions)
                {
                    Debug.LogError($"????: {innerEx.Message}");
                }
            }

            responseHandle?.Invoke(new Dictionary<string, object>()
            {
                ["flush_count"] = eventCount
            });
        }

        public static IEnumerator GetWithFORM_2(string url, string appId, Dictionary<string, object> param,
            ResponseHandle responseHandle)
        {
            responseHandle?.Invoke(new Dictionary<string, object>()
            {
                ["code"] = 0,
                ["data"] = new Dictionary<string, object>()
            });
            yield break;
        }
    }
}
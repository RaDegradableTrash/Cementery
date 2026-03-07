using System.Collections.Generic;
using UnityEngine;

namespace Unity.UOS.Insight.Utils
{
    public class FileJson
    {
        // Save the event, return the number of cached events
        internal static int EnqueueReportingData(Dictionary<string, object> data, string prefix)
        {
            int eventId = EventAutoIncrementingID(prefix);
            string reportingKey = GetEventKeysPrefix(prefix, eventId);

            var dataJson = MiniJson.Serialize(data);
            PlayerPrefs.SetString(reportingKey, dataJson);
            IncreaseReportingDataID(prefix);
            int eventCount = EventAutoIncrementingID(prefix) - EventIndexID(prefix);
            return eventCount;
        }

        // Get event end ID
        internal static int EventAutoIncrementingID(string prefix)
        {
            string mEventAutoIncrementingID = GetEventAutoIncrementingIDKey(prefix);
            return PlayerPrefs.HasKey(mEventAutoIncrementingID) ? PlayerPrefs.GetInt(mEventAutoIncrementingID) : 0;
        }

        // Auto increment event end ID
        private static void IncreaseReportingDataID(string prefix)
        {
            int id = EventAutoIncrementingID(prefix);
            id += 1;
            PlayerPrefs.SetInt(GetEventAutoIncrementingIDKey(prefix), id);
        }

        // Reset event end ID
        private static void ResetReportingDataID(string prefix)
        {
            int id = 0;
            PlayerPrefs.SetInt(GetEventAutoIncrementingIDKey(prefix), id);
        }

        // Get event start ID
        internal static int EventIndexID(string prefix)
        {
            string eventIndexID = GetEventIndexIDKey(prefix);
            return PlayerPrefs.HasKey(eventIndexID) ? PlayerPrefs.GetInt(eventIndexID) : 0;
        }

        // Save time start ID
        private static void SaveEventIndexID(int indexID, string prefix)
        {
            string eventIndexID = GetEventIndexIDKey(prefix);
            PlayerPrefs.SetInt(eventIndexID, indexID);
        }

        // Fetch a specified number of events in batches
        internal static string DequeueBatchReportingData(int batchSize, string prefix, out int eventCount)
        {
            string batchData = eventBatchPrefix;
            List<Dictionary<string, object>> tempDataList = new List<Dictionary<string, object>>();
            int dataIndex = EventIndexID(prefix);
            int maxIndex = EventAutoIncrementingID(prefix) - 1;
            eventCount = 0;
            while (eventCount < batchSize && dataIndex <= maxIndex)
            {
                string reportingKey = GetEventKeysPrefix(prefix, dataIndex);
                if (PlayerPrefs.HasKey(reportingKey))
                {
                    string dataJson = PlayerPrefs.GetString(reportingKey);
                    if (eventCount < batchSize - 1 && dataIndex < maxIndex)
                    {
                        batchData = batchData + dataJson + eventBatchInfix;
                    }
                    else
                    {
                        batchData = batchData + dataJson;
                    }
                    eventCount++;
                }
                dataIndex++;
            }

            if (eventCount > 0)
            {
                batchData = batchData + eventBatchSuffix;
                return batchData;
            }
            else
            {
                return null;
            }
        }

        // Batch delete the specified number of events and return the remaining number of events
        internal static int DeleteBatchReportingData(int batchSize, string prefix)
        {
            int deletedCount = 0;
            int dataIndex = EventIndexID(prefix);
            int maxIndex = EventAutoIncrementingID(prefix) - 1;
            while (deletedCount < batchSize && dataIndex <= maxIndex)
            {
                string reportingKey = GetEventKeysPrefix(prefix, dataIndex);
                if (PlayerPrefs.HasKey(reportingKey))
                {
                    PlayerPrefs.DeleteKey(reportingKey);
                    deletedCount++;
                }
                dataIndex++;
            }
            SaveEventIndexID(dataIndex, prefix);

            int eventCount = EventAutoIncrementingID(prefix) - EventIndexID(prefix);
            return eventCount;
        }

        // Batch delete specified events
        // internal static void DeleteBatchReportingData(List<Dictionary<string, object>> batch, string prefix) {
        //     foreach(Dictionary<string, object> data in batch) {
        //         String id = data["id"].ToString();
        //         if (id != null && PlayerPrefs.HasKey(id)) {
        //             PlayerPrefs.DeleteKey(id);
        //         }
        //     }
        // }

        // Batch delete all events
        internal static int DeleteAllReportingData(string prefix)
        {
            DeleteBatchReportingData(int.MaxValue, prefix);
            SaveEventIndexID(0, prefix);
            ResetReportingDataID(prefix);
            return 0;
        }

        private static string eventKeyInfix = "Event";
        private static string eventIndexIDSuffix = "EventIndexID";
        private static string eventAutoIncrementingIDSuffix = "EventAutoIncrementingID";

        private static string eventBatchPrefix = "[";
        private static string eventBatchInfix = ",";
        private static string eventBatchSuffix = "]";

        private static Dictionary<string, string> eventKeysPrefix = new Dictionary<string, string>() { };
        private static Dictionary<string, string> eventIndexIDKeys = new Dictionary<string, string>() { };
        private static Dictionary<string, string> eventAutoIncrementingIDKeys = new Dictionary<string, string>() { };

        private static string GetEventKeysPrefix(string prefix, int index)
        {
            if (eventKeysPrefix.ContainsKey(prefix))
            {
                return eventKeysPrefix[prefix] + index;
            }
            else
            {
                string eventKey = prefix + eventKeyInfix;
                eventKeysPrefix[prefix] = eventKey;
                return eventKey + index;
            }
        }

        private static string GetEventIndexIDKey(string prefix)
        {
            if (eventIndexIDKeys.ContainsKey(prefix))
            {
                return eventIndexIDKeys[prefix];
            }
            else
            {
                string eventKey = prefix + eventIndexIDSuffix;
                eventIndexIDKeys[prefix] = eventKey;
                return eventKey;
            }
        }

        private static string GetEventAutoIncrementingIDKey(string prefix)
        {
            if (eventAutoIncrementingIDKeys.ContainsKey(prefix))
            {
                return eventAutoIncrementingIDKeys[prefix];
            }
            else
            {
                string eventKey = prefix + eventAutoIncrementingIDSuffix;
                eventAutoIncrementingIDKeys[prefix] = eventKey;
                return eventKey;
            }
        }
    }
}
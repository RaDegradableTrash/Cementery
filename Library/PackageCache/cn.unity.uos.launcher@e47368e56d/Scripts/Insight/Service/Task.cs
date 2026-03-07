using System.Collections;
using System.Collections.Generic;
using Unity.UOS.Insight.Service.Request;
using Unity.UOS.Insight.Utils;
using UnityEngine;

namespace Unity.UOS.Insight.Service
{
    [DisallowMultipleComponent]
    public class Task : MonoBehaviour
    {
        private readonly static object _locker = new object();

        private List<AnalyticsRequest> requestList = new List<AnalyticsRequest>();
        private List<ResponseHandle> responseHandleList = new List<ResponseHandle>();
        private List<int> batchSizeList = new List<int>();
        private List<string> appIdList = new List<string>();


        private static Task mSingleTask;

        private bool isWaiting = false;
        private float updateInterval = 0;

        public static Task SingleTask()
        {
            return mSingleTask;
        }

        private void Awake()
        {
            mSingleTask = this;
        }

        private void Start()
        {
        }

        private void Update()
        {
            updateInterval += UnityEngine.Time.deltaTime;
            if (updateInterval > 0.2)
            {
                updateInterval = 0;
                if (!isWaiting && requestList.Count > 0)
                {
                    WaitOne();
                    StartRequestSendData();
                }
            }
        }

        /// <summary>
        /// hold signal
        /// </summary>
        public void WaitOne()
        {
            isWaiting = true;
        }
        /// <summary>
        /// release signal
        /// </summary>
        public void Release()
        {
            isWaiting = false;
        }
        public void SyncInvokeAllTask()
        {

        }

        public void StartRequest(AnalyticsRequest mRequest, ResponseHandle responseHandle, int batchSize, string appId)
        {
            lock (_locker)
            {
                requestList.Add(mRequest);
                responseHandleList.Add(responseHandle);
                batchSizeList.Add(batchSize);
                appIdList.Add(appId);
            }
        }

        private void StartRequestSendData()
        {
            if (requestList.Count > 0)
            {
                AnalyticsRequest mRequest;
                ResponseHandle responseHandle;
                string list;
                int eventCount = 0;
                lock (_locker)
                {
                    mRequest = requestList[0];
                    responseHandle = responseHandleList[0];
                    list = FileJson.DequeueBatchReportingData(batchSizeList[0], appIdList[0], out eventCount);
                }
                if (mRequest != null)
                {
                    if (eventCount > 0 && list.Length > 0)
                    {
                        this.StartCoroutine(this.SendData(mRequest, responseHandle, list, eventCount));
                    }
                    else
                    {
                        if (responseHandle != null)
                        {
                            responseHandle(null);
                        }
                    }
                    lock (_locker)
                    {
                        requestList.RemoveAt(0);
                        responseHandleList.RemoveAt(0);
                        batchSizeList.RemoveAt(0);
                        appIdList.RemoveAt(0);
                    }
                }

            }
        }
        private IEnumerator SendData(AnalyticsRequest mRequest, ResponseHandle responseHandle, string list, int eventCount)
        {
            yield return AnalyticsRequest.SendData_2(responseHandle, list, eventCount);
        }
    }
}

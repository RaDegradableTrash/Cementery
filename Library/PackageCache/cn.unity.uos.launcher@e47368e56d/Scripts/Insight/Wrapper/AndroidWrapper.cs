#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Unity.UOS.Insight.Wrapper
{
    public partial class BaseWrapper
    {
        private static TaskCompletionSource<string> _oaidTcs;

        public class OAIDCallbackListener : AndroidJavaProxy
        {
            public OAIDCallbackListener() : base("com.github.gzuliyujiang.oaid.IGetter") { }

            void onOAIDGetComplete(string result)
            {
                Debug.Log("Get OAID Complete: " + result + "\n");

                _oaidTcs?.TrySetResult(result);
            }

            void onOAIDGetError(AndroidJavaObject error)
            {
                Debug.Log("Get OAID Error:" + error + "\n");

                _oaidTcs?.TrySetException(new Exception("Failed to fetch OAID: " + error));
            }
        }

        public static async Task<string> FetchOAIDAsync()
        {
            _oaidTcs = new TaskCompletionSource<string>();

            AndroidJavaObject context = new AndroidJavaClass("com.unity3d.player.UnityPlayer").GetStatic<AndroidJavaObject>("currentActivity");

            AndroidJavaClass OAIDFactory = new AndroidJavaClass("com.github.gzuliyujiang.oaid.DeviceID");
            OAIDFactory.CallStatic("getOAID", context, new OAIDCallbackListener());

            return await _oaidTcs.Task;
        }
    }
}
#endif
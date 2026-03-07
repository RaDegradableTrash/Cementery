#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace Unity.UOS.Insight.Wrapper
{
    public partial class BaseWrapper : MonoBehaviour
    {
        [DllImport("__Internal")]
        private static extern string get_idfv();

        private static TaskCompletionSource<string> _currentTaskCompletionSource;

        delegate void IDFACompletionCallback(string idfa, string errorMsg);

        [AOT.MonoPInvokeCallback(typeof(IDFACompletionCallback))]
        static void getIDFACallback(string idfa, string errorMsg)
        {
            if (_currentTaskCompletionSource != null)
            {
                if (!string.IsNullOrEmpty(errorMsg))
                {
                    Debug.Log($"[IDFA] Callback received - Error: {errorMsg}");
                    _currentTaskCompletionSource.TrySetResult(string.Empty);
                }
                else
                {
                    Debug.Log($"[IDFA] Callback received - IDFA: {idfa}");
                    _currentTaskCompletionSource.TrySetResult(idfa);
                }
                _currentTaskCompletionSource = null;
            }
        }

        [DllImport("__Internal")]
        private static extern void request_idfa(IDFACompletionCallback callback);

        public static string GetIDFV()
        {
            return get_idfv();
        }

        public static async Task<string> GetIDFA()
        {
            _currentTaskCompletionSource = new TaskCompletionSource<string>();

            try
            {
                request_idfa(getIDFACallback);
            }
            catch (Exception ex)
            {
                _currentTaskCompletionSource.TrySetException(ex);
                _currentTaskCompletionSource = null;
            }

            return await _currentTaskCompletionSource.Task;
        }

        void OnDestroy()
        {
            if (_currentTaskCompletionSource != null)
            {
                _currentTaskCompletionSource.TrySetCanceled();
                _currentTaskCompletionSource = null;
            }
        }
    }
}
#endif
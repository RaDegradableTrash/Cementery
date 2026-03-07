#if UNITY_OPENHARMONY && !UNITY_EDITOR
using System;
using System.Threading.Tasks;
using UnityEngine.OpenHarmony;
using UnityEngine;

namespace Unity.UOS.Insight.Wrapper
{
    public partial class BaseWrapper : MonoBehaviour
    {
        private static OpenHarmonyJSClass m_OHADSClass = null;
        private static OpenHarmonyJSCallback m_callback = null;

        private static TaskCompletionSource<string> _currentTaskCompletionSource;

        private static bool _isInitialized = false;
        private static bool _isRequesting = false;
        private static readonly TimeSpan REQUEST_TIMEOUT = TimeSpan.FromSeconds(10);

        public static void Init()
        {
            if (!_isInitialized)
            {
                try
                {
                    m_OHADSClass = new OpenHarmonyJSClass("OHADSHelper");
                    m_callback = new OpenHarmonyJSCallback(CallbackFromTS);
                    _isInitialized = true;
                    Debug.Log("[OAID] OpenHarmony ADS initialized successfully");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OAID] Failed to initialize OpenHarmony ADS: {ex.Message}");
                    _isInitialized = false;
                }
            }
        }

        static object CallbackFromTS(params OpenHarmonyJSObject[] args)
        {
            try
            {
                if (args == null || args.Length == 0)
                {
                    Debug.LogError("[OAID] Callback received null or empty arguments");
                    CompleteRequest(string.Empty);
                    return null;
                }

                OpenHarmonyJSObject data = args[0];
                if (data == null)
                {
                    Debug.LogError("[OAID] Callback received null data object");
                    CompleteRequest(string.Empty);
                    return null;
                }

                var oaid = data.Get<string>("oaid");
                Debug.Log($"[OAID] Received OAID: {oaid}");

                CompleteRequest(oaid ?? string.Empty);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OAID] Error in callback: {ex.Message}");
                CompleteRequest(string.Empty);
            }

            return null;
        }

        private static void CompleteRequest(string result)
        {
            _isRequesting = false;

            if (_currentTaskCompletionSource != null && !_currentTaskCompletionSource.Task.IsCompleted)
            {
                _currentTaskCompletionSource.TrySetResult(result);
            }
        }

        public static void GetOAID()
        {
            if (!_isInitialized)
            {
                Debug.LogError("[OAID] OpenHarmony ADS not initialized");
                CompleteRequest(string.Empty);
                return;
            }

            try
            {
                m_OHADSClass.CallStatic("GetOAID", m_callback);
                Debug.Log("[OAID] GetOAID called successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OAID] Error calling GetOAID: {ex.Message}");
                CompleteRequest(string.Empty);
            }
        }

        public static async Task<string> GetOAIDOpenHarmony()
        {
            if (_isRequesting)
            {
                Debug.LogWarning("[OAID] Request already in progress, returning existing task");
                return await _currentTaskCompletionSource.Task;
            }

            _isRequesting = true;
            _currentTaskCompletionSource = new TaskCompletionSource<string>();

            try
            {
                _ = Task.Delay(REQUEST_TIMEOUT).ContinueWith(t =>
                {
                    if (!_currentTaskCompletionSource.Task.IsCompleted)
                    {
                        Debug.LogError("[OAID] Request timeout");
                        CompleteRequest(string.Empty);
                    }
                });

                bool hasPermission = Permission.HasUserAuthorizedPermission("ohos.permission.APP_TRACKING_CONSENT");
                Debug.Log($"[OAID] Permission check: {hasPermission}");

                if (!hasPermission)
                {
                    Debug.Log("[OAID] Requesting APP_TRACKING_CONSENT permission...");
                    var permissionCallbacks = new PermissionCallbacks();
                    bool callbackReceived = false;

                    permissionCallbacks.PermissionGranted += (reason) =>
                    {
                        if (callbackReceived) return;
                        callbackReceived = true;

                        Debug.Log($"[OAID] Permission granted: {reason}");
                        InitializeAndGetOAID();
                    };

                    permissionCallbacks.PermissionDenied += (reason) =>
                    {
                        if (callbackReceived) return;
                        callbackReceived = true;

                        Debug.LogWarning($"[OAID] Permission denied: {reason}");
                        CompleteRequest(string.Empty);
                    };

                    Permission.RequestUserPermission("ohos.permission.APP_TRACKING_CONSENT", permissionCallbacks);
                }
                else
                {
                    Debug.Log("[OAID] Permission already granted, getting OAID...");
                    InitializeAndGetOAID();
                }

                return await _currentTaskCompletionSource.Task;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OAID] Error in GetOAIDOpenHarmony: {ex.Message}");
                CompleteRequest(string.Empty);
                return string.Empty;
            }
        }

        private static void InitializeAndGetOAID()
        {
            try
            {
                Init();
                if (_isInitialized)
                {
                    GetOAID();
                }
                else
                {
                    Debug.LogError("[OAID] Initialization failed, cannot get OAID");
                    CompleteRequest(string.Empty);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OAID] Error in InitializeAndGetOAID: {ex.Message}");
                CompleteRequest(string.Empty);
            }
        }


        void OnDestroy()
        {
            _isRequesting = false;

            if (_currentTaskCompletionSource != null && !_currentTaskCompletionSource.Task.IsCompleted)
            {
                _currentTaskCompletionSource.TrySetCanceled();
                _currentTaskCompletionSource = null;
            }
        }
    }
}
#endif
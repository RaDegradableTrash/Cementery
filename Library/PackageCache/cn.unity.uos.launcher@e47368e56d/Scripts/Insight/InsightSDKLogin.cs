using Unity.UOS.Insight.Service;

namespace Unity.UOS.Insight
{
    public partial class InsightSDK
    {
        /// <summary>
        /// Set account ID.
        /// </summary>
        /// <param name="account">account ID</param>
        public static void Login(string account)
        {
            if (!_initialized)
            {
                _operationCache.Enqueue(() => Login(account));
                return;
            }
            if (!_enabled)
                return;

            if (!string.IsNullOrEmpty(AnalyticsService.AccountID()))
            {
                Logout();
            }

            AnalyticsService.Login(account);
            if (!string.IsNullOrEmpty(account))
            {
                AnalyticsService.Report("login");
            }
        }

        /// <summary>
        /// Clear account ID.
        /// </summary>
        public static void Logout()
        {
            if (!_initialized)
            {
                _operationCache.Enqueue(Logout);
                return;
            }
            if (!_enabled)
                return;

            if (!string.IsNullOrEmpty(AnalyticsService.AccountID()))
            {
                AnalyticsService.Report("logout");
                Flush();
            }

            AnalyticsService.Logout();
        }

        private void OnApplicationQuit()
        {
            Logout();
        }
    }
}
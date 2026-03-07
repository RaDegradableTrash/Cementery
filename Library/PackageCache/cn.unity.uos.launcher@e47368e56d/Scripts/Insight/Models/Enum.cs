using System;

namespace Unity.UOS.Insight.Models
{
    /// <summary>
    /// Time Zone in SDK options
    /// </summary>
    public enum AnalyticsTimeZone
    {
        Local,
        UTC,
        Asia_Shanghai,
        Asia_Tokyo,
        America_Los_Angeles,
        America_New_York,
        Other = 100
    }

    /// <summary>
    /// SDK running mode options
    /// </summary>
    public enum Mode
    {
        Normal = 0,
        Debug = 1,
        DebugOnly = 2
    }

    /// <summary>
    /// Data post options
    /// </summary>
    public enum NetworkType
    {
        Wifi = 2,
        All = 1
    }

    /// <summary>
    /// Auto-reporting Events Type options
    /// </summary>
    [Flags]
    public enum AutoReportEventType
    {
        None = 0,
        AppStart = 1 << 0, // reporting when the app enters the foreground (app_start)
        AppEnd = 1 << 1, // reporting when the app enters the background (app_end)
        AppCrash = 1 << 4, // reporting when an uncaught exception occurs (app_crash)
        AppInstall = 1 << 5, // reporting when the app is opened for the first time after installation (app_install)
        AppSceneLoad = 1 << 6, // reporting when the scene is loaded in the app (scene_loaded)
        AppSceneUnload = 1 << 7, // reporting when the scene is unloaded in the app (scene_loaded)
        All = AppStart | AppEnd | AppInstall | AppCrash | AppSceneLoad | AppSceneUnload
    }

    /// <summary>
    /// Data Reporting Status
    /// </summary>
    public enum ReportStatus
    {
        Pause = 1, // pause data reporting
        Stop = 2, // stop data reporting, and clear caches
        SaveOnly = 3, // data stores in the cache, but not be reported
        Normal = 4 // resume data reporting
    }

    // Crosss Platform
    public enum ThirdPartyType
    {
        NONE = 0,
        APPSFLYER = 1 << 0, // AppsFlyer
        IRONSOURCE = 1 << 1, // IronSource
        ADJUST = 1 << 2, // Adjust
        BRANCH = 1 << 3, // Branch
        TOPON = 1 << 4, // TopOn
        TRACKING = 1 << 5, // ReYun
        TRADPLUS = 1 << 6, // TradPlus
    };

    // SSL
    public enum SSLPinningMode
    {
        NONE = 0, // Only allow certificates trusted by the system
        PUBLIC_KEY = 1 << 0, // Verify public key
        CERTIFICATE = 1 << 1 // Verify all contents
    }
}
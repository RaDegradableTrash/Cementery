using System.Collections.Generic;

namespace Unity.UOS.Insight
{
    /// <summary>
    /// Dynamic super properties interfaces.
    /// </summary>
    public interface DynamicSuperPropertiesHandler
    {
        /// <summary>
        /// Dynamically gets event properties
        /// </summary>
        /// <returns>event properties</returns>
        Dictionary<string, object> GetDynamicSuperProperties();
    }

    /// <summary>
    /// Auto report event callback interfaces.
    /// </summary>
    public interface AutoReportEventHandler
    {
        /// <summary>
        /// Get Auto report event properties
        /// </summary>
        /// <param name="type">auto report event type</param>
        /// <param name="properties">event properties</param>
        /// <returns>event properties</returns>
        Dictionary<string, object> GetAutoReportEventProperties(int type, Dictionary<string, object> properties);
    }
}
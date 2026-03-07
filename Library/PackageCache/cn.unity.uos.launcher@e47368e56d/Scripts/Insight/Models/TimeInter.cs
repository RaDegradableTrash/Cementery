using System;

namespace Unity.UOS.Insight.Models
{
    public interface TimeInter
    {
        string GetTime(TimeZoneInfo timeZone);
        Double GetZoneOffset(TimeZoneInfo timeZone);
    }
}
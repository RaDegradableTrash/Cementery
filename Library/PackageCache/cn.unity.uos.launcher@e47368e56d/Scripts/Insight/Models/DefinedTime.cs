using System;

namespace Unity.UOS.Insight.Models
{
    public class DefinedTime : TimeInter
    {
        private string mTime;
        private double mZoneOffset;
        public DefinedTime(string time,double zoneOffset)
        {
            this.mTime = time;
            this.mZoneOffset = zoneOffset;
        }
        public string GetTime(TimeZoneInfo timeZone)
        {
            return this.mTime;
        }

        public double GetZoneOffset(TimeZoneInfo timeZone)
        {
            return this.mZoneOffset;
        }
    }
}
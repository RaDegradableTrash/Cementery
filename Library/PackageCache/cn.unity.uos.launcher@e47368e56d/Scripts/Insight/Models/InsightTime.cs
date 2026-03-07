using System;
using Unity.UOS.Insight.Utils;

namespace Unity.UOS.Insight.Models
{
    public class InsightTime : TimeInter
    {
        private TimeZoneInfo mTimeZone;
        private DateTime mDate;

        public InsightTime(TimeZoneInfo timezone, DateTime date)
        {
            this.mTimeZone = timezone;
            this.mDate = date;
        }

        public string GetTime(TimeZoneInfo timeZone)
        {
            if (timeZone == null)
            {
                return CommonUtil.FormatDate(mDate, mTimeZone);
            }
            else
            {
                return CommonUtil.FormatDate(mDate, timeZone);
            }
        }

        public double GetZoneOffset(TimeZoneInfo timeZone)
        {
            if (timeZone == null)
            {
                return CommonUtil.ZoneOffset(mDate, mTimeZone);
            }
            else
            {
                return CommonUtil.ZoneOffset(mDate, timeZone);
            }
        }
    }

}
using System;
using Unity.UOS.Insight.Utils;

namespace Unity.UOS.Insight.Models
{
    public class CalibratedTime : TimeInter
    {
        private TimeCalibration mCalibratedTime;
        private long mSystemElapsedRealtime;
        private TimeZoneInfo mTimeZone;
        private DateTime mDate;
        public CalibratedTime(TimeCalibration calibrateTimeInter,TimeZoneInfo timeZoneInfo)
        {
            this.mCalibratedTime = calibrateTimeInter;
            this.mTimeZone = timeZoneInfo;
            this.mDate = mCalibratedTime.NowDate();
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

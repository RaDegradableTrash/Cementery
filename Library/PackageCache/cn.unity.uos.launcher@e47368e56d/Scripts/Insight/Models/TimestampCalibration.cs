using System;
using Unity.UOS.Insight.Utils;

namespace Unity.UOS.Insight.Models
{
    public class TimestampCalibration : TimeCalibration
    {

        public TimestampCalibration(long timestamp)
        {
            DateTime dateTimeUtcNow = DateTime.UtcNow;
            this.mStartTime = timestamp;
            this.mSystemElapsedRealtime = Environment.TickCount;

            double time_offset = (ConvertDateTimeInt(dateTimeUtcNow) - timestamp) / 1000;
            if (Log.GetEnable()) Log.i("Time Calibration with NTP (" + timestamp + "), diff = " + time_offset.ToString("0.000s"));
        }
    }
}

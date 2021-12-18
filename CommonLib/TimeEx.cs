global using TimeT64 = System.Int64;
global using TimeT = System.Int32;

using System;
using System.Collections.Generic;
using System.Text;

namespace CommonLib
{
    public static class TimeEx
    {
        public static TimeT64 GetTimeT64() => DateTimeOffset.Now.ToUnixTimeMilliseconds();
        public static TimeT GetTimeT() => (TimeT)DateTimeOffset.Now.ToUnixTimeSeconds();

        public static readonly TimeT64 Duration_Sec_To_Ms = (TimeT64)TimeSpan.FromSeconds(1).TotalMilliseconds;
        public static readonly TimeT64 Duration_Minute_To_Ms = (TimeT64)TimeSpan.FromMinutes(1).TotalMilliseconds;
        public static readonly TimeT64 Duration_Hour_To_Ms = (TimeT64)TimeSpan.FromHours(1).TotalMilliseconds;
        public static readonly TimeT64 Duration_Day_To_Ms = (TimeT64)TimeSpan.FromDays(1).TotalMilliseconds;
        public static readonly TimeT64 Duration_Year_To_Ms = (TimeT64)TimeSpan.FromDays(356).TotalMilliseconds;
    }
}

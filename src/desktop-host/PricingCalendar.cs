using System;
using System.Collections.Generic;

namespace DshTray
{
    internal enum PricingSlot
    {
        Peak,
        Valley,
        Unknown
    }

    internal static class PricingCalendar
    {
        private static readonly HashSet<string> Holidays = new HashSet<string>
        {
            "2026-01-01", "2026-01-02", "2026-01-03",
            "2026-02-15", "2026-02-16", "2026-02-17", "2026-02-18",
            "2026-02-19", "2026-02-20", "2026-02-21", "2026-02-22", "2026-02-23",
            "2026-04-04", "2026-04-05", "2026-04-06",
            "2026-05-01", "2026-05-02", "2026-05-03", "2026-05-04", "2026-05-05",
            "2026-06-19", "2026-06-20", "2026-06-21",
            "2026-09-25", "2026-09-26", "2026-09-27",
            "2026-10-01", "2026-10-02", "2026-10-03", "2026-10-04",
            "2026-10-05", "2026-10-06", "2026-10-07"
        };

        private static readonly HashSet<string> MakeupWorkdays = new HashSet<string>
        {
            "2026-01-04", "2026-02-14", "2026-02-28",
            "2026-05-09", "2026-09-20", "2026-10-10"
        };

        private static readonly HashSet<int> KnownYears = new HashSet<int> { 2026 };

        public static PricingSlot CurrentSlot()
        {
            DateTime beijing = DateTime.UtcNow.AddHours(8);
            return SlotAt(beijing);
        }

        public static PricingSlot SlotAt(DateTime beijingTime)
        {
            if (!KnownYears.Contains(beijingTime.Year))
                return PricingSlot.Unknown;

            string key = beijingTime.ToString("yyyy-MM-dd");

            if (Holidays.Contains(key))
                return PricingSlot.Valley;

            if (MakeupWorkdays.Contains(key))
            {
                // falls through to time-of-day check below
            }
            else if (beijingTime.DayOfWeek == DayOfWeek.Saturday
                  || beijingTime.DayOfWeek == DayOfWeek.Sunday)
            {
                return PricingSlot.Valley;
            }

            int minutes = beijingTime.Hour * 60 + beijingTime.Minute;
            bool peak = (minutes >= 9 * 60 && minutes < 12 * 60)
                     || (minutes >= 14 * 60 && minutes < 18 * 60);
            return peak ? PricingSlot.Peak : PricingSlot.Valley;
        }

        public static string SlotText(PricingSlot slot)
        {
            if (slot == PricingSlot.Peak) return "峰";
            if (slot == PricingSlot.Valley) return "谷";
            return "";
        }
    }
}

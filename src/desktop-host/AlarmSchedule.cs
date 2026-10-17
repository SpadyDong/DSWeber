using System;
using System.Collections.Generic;
using System.Globalization;

namespace DshTray
{
    internal static class AlarmSchedule
    {
        // Fire only while the configured minute is current; missed minutes are skipped.
        public static List<string> DueMessages(DateTime previous, DateTime now, List<AlarmItem> alarms)
        {
            var messages = new List<string>();
            if (alarms == null || now <= previous) return messages;
            DateTime minuteStart = new DateTime(now.Year, now.Month, now.Day,
                now.Hour, now.Minute, 0);
            if (previous >= minuteStart) return messages;

            foreach (var alarm in alarms)
            {
                if (alarm == null || !alarm.Enabled || string.IsNullOrWhiteSpace(alarm.Message)) continue;
                TimeSpan time;
                if (!TimeSpan.TryParseExact((alarm.Time ?? "").Trim(), @"hh\:mm",
                    CultureInfo.InvariantCulture, out time)) continue;

                if (time.Hours == now.Hour && time.Minutes == now.Minute)
                    messages.Add(alarm.Time.Trim() + "  " + alarm.Message);
            }
            return messages;
        }
    }
}

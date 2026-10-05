using System;

namespace RatHabitat
{
    /// <summary>
    /// Player-facing calendar labels derived from the existing elapsed game
    /// clock. The calendar uses a stable 365-day year and Gregorian month
    /// lengths without leap years, so a saved timestamp always maps to the
    /// same displayed date.
    /// </summary>
    public static class GameCalendar
    {
        private static readonly string[] MonthNames =
        {
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December",
        };

        private static readonly int[] DaysPerMonth =
        {
            31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31,
        };

        public static string FormatDate(long gameTimeMs)
        {
            long elapsedDay = Math.Max(0L, gameTimeMs) / GameConfig.GameDayMs;
            long year = elapsedDay / 365L;
            int dayOfYear = (int)(elapsedDay % 365L);
            int month = 0;
            while (month < DaysPerMonth.Length - 1 && dayOfYear >= DaysPerMonth[month])
            {
                dayOfYear -= DaysPerMonth[month];
                month++;
            }

            int day = dayOfYear + 1;
            return "Year " + year + " - " + MonthNames[month] + " " + day + OrdinalSuffix(day);
        }

        public static string FormatTimestamp(long gameTimeMs)
        {
            long safeTime = Math.Max(0L, gameTimeMs);
            long dayTime = safeTime % GameConfig.GameDayMs;
            int hours = (int)(dayTime / (60L * 60L * 1000L));
            int minutes = (int)((dayTime / (60L * 1000L)) % 60L);
            return FormatDate(safeTime) + "  •  " + hours.ToString("00") + ":" + minutes.ToString("00");
        }

        public static string OrdinalSuffix(int value)
        {
            int lastTwo = value % 100;
            if (lastTwo >= 11 && lastTwo <= 13) return "th";
            switch (value % 10)
            {
                case 1: return "st";
                case 2: return "nd";
                case 3: return "rd";
                default: return "th";
            }
        }
    }
}

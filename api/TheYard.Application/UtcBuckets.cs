// How an instant is cut into the UTC buckets the stores key and partition on: the day as text,
// the hour and the minute. One copy, so the site activity, the kept log, the machine history and
// the cost history all agree on where one day ends and the next begins.
using System.Globalization;

namespace TheYard.Application;

/// <summary>The UTC day, hour and minute an instant falls in, read as properties of the instant.</summary>
public static class UtcBuckets
{
    extension(DateTimeOffset at)
    {
        /// <summary>The UTC day as yyyy-MM-dd, the same text in every culture, which is what the stores partition on.</summary>
        public string UtcDay => at.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>The start of the UTC hour this instant falls in.</summary>
        public DateTimeOffset UtcHour
        {
            get
            {
                var utc = at.ToUniversalTime();
                return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
            }
        }

        /// <summary>The start of the UTC minute this instant falls in.</summary>
        public DateTimeOffset UtcMinute
        {
            get
            {
                var utc = at.ToUniversalTime();
                return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
            }
        }
    }
}

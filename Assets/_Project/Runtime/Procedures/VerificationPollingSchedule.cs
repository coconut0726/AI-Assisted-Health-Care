using System;

namespace HomeCare.Procedures
{
    /// <summary>Clock-driven polling slots. Busy slots are skipped, never queued or replayed.</summary>
    public sealed class VerificationPollingSchedule
    {
        public bool Running { get; private set; }
        public double NextDue { get; private set; }

        public void Start(double now, double interval)
        {
            NextDue = now + CheckedInterval(interval);
            Running = true;
        }

        public void Stop() => Running = false;

        public bool TryConsumeTick(double now, double interval, bool canVerify)
        {
            if (!Running || now < NextDue) return false;
            // Use the current time after a stall: do not catch up with a burst of requests.
            NextDue = now + CheckedInterval(interval);
            return canVerify;
        }

        static double CheckedInterval(double interval)
        {
            if (double.IsNaN(interval) || double.IsInfinity(interval) || interval <= 0)
                throw new ArgumentOutOfRangeException(nameof(interval));
            return interval;
        }
    }
}

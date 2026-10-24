using System;
using System.Collections;

namespace pitTeam.Modules
{
    // Keeps Unity's one scheduled coroutine alive, replacing only a failed native iterator.
    internal sealed class SainVisionRecoveryEnumerator : IEnumerator, IDisposable
    {
        private IEnumerator? current;
        private readonly Func<IEnumerator> create;
        private readonly Func<bool> alive;
        private readonly Func<float> clock;
        private readonly object retryWait;
        private readonly Action<Exception, int> report;
        private bool stopped;
        private float nextReport = float.NegativeInfinity;
        private int failures;

        internal SainVisionRecoveryEnumerator(IEnumerator initial, Func<IEnumerator> create,
            Func<bool> alive, Func<float> clock, object retryWait, Action<Exception, int> report)
        {
            current = initial;
            this.create = create;
            this.alive = alive;
            this.clock = clock;
            this.retryWait = retryWait;
            this.report = report;
        }

        public object? Current { get; private set; }

        public bool MoveNext()
        {
            if (stopped) return false;
            try
            {
                if (!alive())
                {
                    Dispose();
                    return false;
                }
                current ??= create();
                if (!current.MoveNext())
                {
                    // Normal native completion is teardown, not a reason to restart.
                    Dispose();
                    return false;
                }
                Current = current.Current;
                return true;
            }
            catch (Exception ex)
            {
                Report(ex);
                ReleaseIterator();
                Current = retryWait;
                return true;
            }
        }

        private void Report(Exception exception)
        {
            failures++;
            try
            {
                float now = clock();
                if (now < nextReport) return;
                nextReport = now + 30f;
                report(exception, failures);
            }
            catch { /* Diagnostics must not kill recovery. */ }
        }

        private void ReleaseIterator()
        {
            IEnumerator? previous = current;
            current = null;
            try { (previous as IDisposable)?.Dispose(); }
            catch (Exception ex) { Report(ex); }
        }

        public void Dispose()
        {
            if (stopped) return;
            stopped = true;
            Current = null;
            ReleaseIterator();
        }

        public void Reset() => throw new NotSupportedException();
    }
}

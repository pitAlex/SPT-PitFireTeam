using System;
using System.Diagnostics;
using System.Threading;

namespace pitTeam.Modules
{
    // Inventory commit remains synchronous. This bounds the asynchronous work around it.
    internal sealed class GearSwapApplyScope : IDisposable
    {
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private readonly int _timeoutMs;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly CancellationTokenSource _preparation;
        private readonly CancellationToken _lifetimeToken, _preparationToken;
        private bool _stopped, _disposed;
        internal bool Committed { get; private set; }
        internal bool Active => !_lifetimeToken.IsCancellationRequested;
        internal CancellationToken Lifetime => _lifetimeToken;
        internal CancellationToken Preparation => _preparationToken;
        internal int RemainingMs => Math.Max(1, _timeoutMs - (int)_elapsed.ElapsedMilliseconds);

        internal GearSwapApplyScope(int timeoutMs = 30000)
        {
            _timeoutMs = timeoutMs;
            _preparation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _lifetimeToken = _lifetime.Token;
            _preparationToken = _preparation.Token;
            _lifetime.CancelAfter(timeoutMs);
        }

        internal void CancelPreparation() { if (!_disposed) _preparation.Cancel(); }
        internal void Commit()
        {
            // Once synchronous replay succeeds, cancellation cannot undo its ownership result.
            Committed = true;
        }
        internal void Stop()
        {
            if (_stopped) return;
            _stopped = true;
            _lifetime.Cancel();
        }
        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
            _preparation.Dispose();
            _lifetime.Dispose();
        }
    }
}

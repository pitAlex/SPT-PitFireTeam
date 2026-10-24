using System;
using System.Collections.Generic;

namespace pitTeam.Modules
{
    /// <summary>Runs on Unity's thread without yielding. Publication is deliberately outside this boundary.</summary>
    internal static class GearSwapTransaction
    {
        internal static List<T> Execute<T>(IEnumerable<Func<T>> edits, Action<T> rollback, Action validate)
        {
            var applied = new List<T>();
            try
            {
                foreach (Func<T> edit in edits) applied.Add(edit());
                validate();
                return applied;
            }
            catch (Exception cause)
            {
                var errors = new List<Exception> { cause };
                for (int i = applied.Count - 1; i >= 0; i--)
                {
                    try { rollback(applied[i]); }
                    catch (Exception failure) { errors.Add(failure); }
                }
                if (errors.Count > 1) throw new AggregateException("Gear exchange rollback failed.", errors);
                throw;
            }
        }
    }
}

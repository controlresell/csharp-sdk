using System;
using System.Threading;

namespace ControlResell.Sdk
{
    /// <summary>
    /// Chooses the <c>Idempotency-Key</c> of the <c>POST</c> calls made inside the scope. Without a scope the SDK generates
    /// a UUID v4 for each call; either way the key is the same on every retry of the call.
    /// </summary>
    /// <example>
    /// <code>
    /// using (IdempotencyScope.Use("import-2026-10-26-item-42"))
    /// {
    ///     await client.Items.PublishItemAsync(42, request);
    /// }
    /// </code>
    /// </example>
    public static class IdempotencyScope
    {
        private static readonly AsyncLocal<string?> Current = new AsyncLocal<string?>();

        internal static string? Key => Current.Value;

        /// <summary>Uses <paramref name="key"/> for the <c>POST</c> calls made until the returned scope is disposed.</summary>
        public static IDisposable Use(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("The idempotency key must not be empty.", nameof(key));
            var previous = Current.Value;
            Current.Value = key;
            return new Restore(previous);
        }

        private sealed class Restore : IDisposable
        {
            private readonly string? _previous;
            private bool _disposed;

            internal Restore(string? previous) => _previous = previous;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Current.Value = _previous;
            }
        }
    }
}

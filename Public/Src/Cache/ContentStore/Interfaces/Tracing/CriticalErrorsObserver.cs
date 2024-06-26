// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Interfaces.Tracing
{
    /// <summary>
    /// Global handler for observing critical and non-critical exception that occur in a cache code.
    /// </summary>
    /// <remarks>
    /// This is used in the BuildXL executable to fail-fast on cache critical failures.
    /// </remarks>
    public static class CriticalErrorsObserver
    {
        /// <summary>
        /// A global event that is raised when a critical error occurred in a cache code.
        /// </summary>
        /// <remarks>
        /// Be aware, that this is a static event, meaning that if a short-lived object will subscribe and not unsubscribe from it,
        /// it will cause effectively a memory leak.
        /// </remarks>
        public static event EventHandler<CriticalErrorEventArgs>? OnCriticalError;

        /// <nodoc />
        public static void RaiseCriticalError(ResultBase result)
        {
            OnCriticalError?.Invoke(null, new CriticalErrorEventArgs(result));
        }

        /// <summary>
        /// A global event that is raised when a non-critical error occurred in a cache code.
        /// </summary>
        /// <remarks>
        /// Be aware, that this is a static event, meaning that if a short-lived object will subscribe and not unsubscribe from it,
        /// it will cause effectively a memory leak.
        /// </remarks>
        public static event EventHandler<RecoverableErrorEventArgs>? OnRecoverableError;

        /// <nodoc />
        public static void RaiseRecoverableError(ResultBase result)
        {
            OnRecoverableError?.Invoke(null, new RecoverableErrorEventArgs(result));
        }
    }
}

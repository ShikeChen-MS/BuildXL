// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
#nullable enable
namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Helpers for checking equality and hash codes.
    /// </summary>
    public static class EqualityHelper
    {
        /// <summary>
        ///     Create a hash code for a set of reference types.
        /// </summary>
        public static int GetCombinedHashCode(params object[] args)
        {
            var result = 23;
            unchecked
            {
                foreach (var x in args)
                {
                    result += x is null ? 0 : x.GetHashCode() * 17;
                }
            }

            return result;
        }

        /// <summary>
        /// Gets a hash code for a given sequence.
        /// </summary>
        public static int SequenceHashCode<TSource>(this IEnumerable<TSource>? sequence, IEqualityComparer<TSource>? comparer = null, int limit = 4)
        {
            if (sequence == null)
            {
                return 0;
            }

            if (comparer == null)
            {
                comparer = EqualityComparer<TSource>.Default;
            }

            int result = 0;

            foreach (var e in sequence.Take(limit))
            {
                unchecked
                {
                    result = (result * 397) ^ comparer.GetHashCode(e!);
                }
            }

            return result;
        }

        /// <summary>
        /// Returns true if two sequences are equal.
        /// </summary>
        public static bool SequenceEqual<TSource>(this IEnumerable<TSource>? first, IEnumerable<TSource>? second, IEqualityComparer<TSource>? comparer = null)
        {
            if (comparer == null)
            {
                comparer = EqualityComparer<TSource>.Default;
            }

            if (ReferenceEquals(first, second))
            {
                return true;
            }

            if (first is null)
            {
                return false;
            }

            if (second is null)
            {
                return false;
            }

            using (IEnumerator<TSource> enumerator = first.GetEnumerator())
            {
                using (IEnumerator<TSource> enumerator2 = second.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        if (!enumerator2.MoveNext() || !comparer.Equals(enumerator.Current, enumerator2.Current))
                        {
                            return false;
                        }
                    }

                    if (enumerator2.MoveNext())
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}

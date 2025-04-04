﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Pog.Utils;

internal static class EnumerableExtensions {
    public static void Drain<T>(this IEnumerable<T> enumerable) {
        foreach (var _ in enumerable) {}
    }

    public static IEnumerable<TOut> SelectOptional<TIn, TOut>(this IEnumerable<TIn> enumerable, Func<TIn, TOut?> selector)
            where TOut : class {
        return enumerable.Select(selector).WhereNotNull();
    }

    public static IEnumerable<TOut> SelectOptional<TIn, TOut>(this IEnumerable<TIn> enumerable,
            Func<TIn, int, TOut?> selector)
            where TOut : class {
        return enumerable.Select(selector).WhereNotNull();
    }

    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> enumerable) where T : class {
        return enumerable.Where(e => e != null).Select(e => e!);
    }

    public static List<T> Split<T>(this IEnumerable<T> enumerable, Predicate<T> predicate, out List<T> failedValues) {
        failedValues = [];
        var passedValues = new List<T>();
        foreach (var value in enumerable) {
            (predicate(value) ? passedValues : failedValues).Add(value);
        }
        return passedValues;
    }

    public static (List<T> passed, List<T> failed) Split<T>(this IEnumerable<T> enumerable, Predicate<T> predicate) {
        var passedValues = new List<T>();
        var failedValues = new List<T>();
        foreach (var value in enumerable) {
            (predicate(value) ? passedValues : failedValues).Add(value);
        }
        return (passedValues, failedValues);
    }

    /// Convert IAsyncEnumerable to IEnumerable by synchronously blocking.
    public static IEnumerable<T> ToBlockingEnumerable<T>(
            this IAsyncEnumerable<T> enumerable, CancellationToken token = default) {
        // manual expansion of `await foreach`
        var it = enumerable.GetAsyncEnumerator(token);
        try {
            while (it.MoveNextAsync().Preserve().GetAwaiter().GetResult()) {
                yield return it.Current;
            }
        } finally {
            // note that if `.MoveNextAsync()` synchronously throws an exception (e.g. some assembly cannot be loaded),
            //  `.DisposeAsync()` fails with `NotSupportedException`, because we try to dispose the iterator while it still
            //  has unfinished `.MoveNextAsync()` invocation
            it.DisposeAsync().Preserve().GetAwaiter().GetResult();
        }
    }
}

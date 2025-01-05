using System;
using System.Collections.Generic;
using System.Linq;

namespace Pog.Utils;

internal static class EnumerableExtensions {
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
}

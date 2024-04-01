using System.Collections.Generic;

namespace Pog.Utils;

internal static class KeyValuePairExtensions {
    public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> keyValuePair,
            out TKey key, out TValue value) {
        key = keyValuePair.Key;
        value = keyValuePair.Value;
    }
}

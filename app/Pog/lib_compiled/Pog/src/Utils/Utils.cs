namespace Pog.Utils;

public static class ByteArrayExtensions {
    // custom re-implementation of `Convert.ToHexString`, which is not available in .netstandard2.0
    public static unsafe string ToHexString(this byte[] bytes) {
        var len = bytes.Length * 2;
        var output = len > 128 ? new char[len] : stackalloc char[len];
        for (int i = 0, t = 0; i < bytes.Length; i++, t += 2) {
            var b = bytes[i];
            output[t] = "0123456789ABCDEF"[b >> 4];
            output[t + 1] = "0123456789ABCDEF"[b & 0b1111];
        }
        return output.ToString();
    }
}

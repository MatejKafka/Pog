using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Pog;

internal struct HashtableParser {
    public bool IgnoreRequired = false;
    public IEnumerable<object> ExtraKeys => _extraKeys;
    public readonly List<string> Issues;
    public bool HasIssues => Issues.Count > 0;
    public readonly string ObjectPath;

    private readonly Hashtable _raw;
    private readonly HashSet<object> _extraKeys;

    public HashtableParser(Hashtable raw, string objectPath = "", List<string>? issuesList = null) {
        Issues = issuesList ?? new();
        _raw = raw;
        _extraKeys = new(_raw.Keys.Cast<object>());
        ObjectPath = objectPath;
    }

    public void AddIssue(string issueMessage) {
        Issues.Add(issueMessage);
    }

    public void AddValidityIssue(string propertyName, string value, string validDescription) {
        AddIssue($"Invalid '{ObjectPath}{propertyName}' value: '{value}' ({validDescription})");
    }

    internal class DataFileParseException : Exception {
        public DataFileParseException(string message) : base(message) {}
    }

    private string GetKeyName(string key, bool required) {
        return $"{(required ? "Required" : "Optional")} property '{ObjectPath}{key}'";
    }

    private object GetPropertyInternal(string key, bool required, string typeStr) {
        _extraKeys.Remove(key);
        var value = _raw[key];
        if (value != null) {
            return value;
        } else if (!_raw.ContainsKey(key)) {
            if (required && !IgnoreRequired) {
                throw new DataFileParseException($"Missing required manifest property '{key}' of type '{typeStr}'.");
            } else {
                throw new KeyNotFoundException(); // silently handled and hidden
            }
        } else {
            throw new DataFileParseException(
                    $"{GetKeyName(key, required)} is present, but set to $null, expected '{typeStr}'.");
        }
    }

    public object? GetProperty(string key, bool required, string typeStr) {
        try {
            return GetPropertyInternal(key, required, typeStr);
        } catch (DataFileParseException e) {
            AddIssue(e.Message);
        } catch (KeyNotFoundException) {}
        return null;
    }

    private T ParseScalarInternal<T>(string key, bool required) {
        var value = GetPropertyInternal(key, required, typeof(T).ToString());
        if (value is T v) {
            return v;
        } else {
            throw new DataFileParseException($"{GetKeyName(key, required)} is present, " +
                                             $"but has an incorrect type '{value.GetType()}', expected '{typeof(T)}'.");
        }
    }

    private T[] ParseListInternal<T>(string key, bool required) {
        var value = GetPropertyInternal(key, required, typeof(T).ToString());
        if (value is T v) {
            return new[] {v}; // single item, wrap into an array
        } else if (value is not Array) {
            throw new DataFileParseException($"{GetKeyName(key, required)} is present, " +
                                             $"but has an incorrect type '{value.GetType()}', expected '{typeof(T[])}'.");
        }

        var arr = (Array) value;

        // PowerShell types any array literal as object[], check type of each item separately
        var parsed = new T[arr.Length];
        for (var i = 0; i < arr.Length; i++) {
            var item = arr.GetValue(i);
            if (item is T v2) {
                parsed[i] = v2;
            } else {
                var description = item == null ? "is set to $null" : $"({item}) has an incorrect type '{item.GetType()}'";
                throw new DataFileParseException(
                        $"{GetKeyName(key, required)} is present, but item #{i} {description}, expected '{typeof(T)}'.");
            }
        }
        return parsed;
    }

    // ReSharper disable once UnusedTypeParameter, ClassNeverInstantiated.Global
    public class ClassConstraint<T> where T : class {}

    // dummy parameter needed, see https://codeblog.jonskeet.uk/2010/11/02/evil-code-overload-resolution-workaround/
    public T? ParseScalar<T>(string key, bool required, ClassConstraint<T>? dummy = null) where T : class {
        try {
            return ParseScalarInternal<T>(key, required);
        } catch (DataFileParseException e) {
            AddIssue(e.Message);
        } catch (KeyNotFoundException) {}
        return null;
    }

    public T? ParseScalar<T>(string key, bool required, T? dummy = default) where T : struct {
        try {
            return ParseScalarInternal<T>(key, required);
        } catch (DataFileParseException e) {
            AddIssue(e.Message);
        } catch (KeyNotFoundException) {}
        return null;
    }

    public T[]? ParseList<T>(string key, bool required) {
        try {
            return ParseListInternal<T>(key, required);
        } catch (DataFileParseException e) {
            AddIssue(e.Message);
        } catch (KeyNotFoundException) {}
        return null;
    }
}

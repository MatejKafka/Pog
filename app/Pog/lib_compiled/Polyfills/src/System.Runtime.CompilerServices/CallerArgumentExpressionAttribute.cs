// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// ReSharper disable once CheckNamespace

namespace System.Runtime.CompilerServices {
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class CallerArgumentExpressionAttribute(string parameterName) : Attribute {
        public string ParameterName {get;} = parameterName;
    }
}
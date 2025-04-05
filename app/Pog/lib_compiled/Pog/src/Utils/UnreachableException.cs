using System;

namespace Pog.Utils;

/// Polyfill for System.Diagnostics.UnreachableException that was added in .NET 7.
public class UnreachableException : Exception;

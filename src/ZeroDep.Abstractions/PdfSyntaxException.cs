using System;

namespace ZeroDep.Abstractions;

/// <summary>Thrown when PDF byte syntax cannot be parsed into a valid object structure.</summary>
public sealed class PdfSyntaxException : Exception
{
    /// <summary>Initializes a new instance.</summary>
    public PdfSyntaxException()
    {
    }

    /// <summary>Initializes a new instance with the specified message.</summary>
    /// <param name="message">A description of the syntax error.</param>
    public PdfSyntaxException(string message) : base(message)
    {
    }

    /// <summary>Initializes a new instance with the specified message and inner exception.</summary>
    /// <param name="message">A description of the syntax error.</param>
    /// <param name="innerException">The underlying cause.</param>
    public PdfSyntaxException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

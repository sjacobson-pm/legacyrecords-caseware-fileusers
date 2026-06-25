using System;
using System.Diagnostics.CodeAnalysis;

namespace LegacyRecordsCaseWareFileUsers.Exceptions;

/// <summary>
///     Thrown when the users assigned to a CaseWare file's security group cannot be retrieved.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Exception type with no behavior to test.")]
public class CaseWareFileUserRetrievalException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="CaseWareFileUserRetrievalException" /> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public CaseWareFileUserRetrievalException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="CaseWareFileUserRetrievalException" /> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of this exception.</param>
    public CaseWareFileUserRetrievalException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

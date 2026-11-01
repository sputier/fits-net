namespace Eleris.FitsNet;

/// <summary>Thrown when data does not conform to the FITS standard.</summary>
public class FitsException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public FitsException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public FitsException(string message, Exception innerException) : base(message, innerException) { }
}

namespace FitsNet;

/// <summary>Thrown when data does not conform to the FITS standard.</summary>
public class FitsException : Exception
{
    public FitsException(string message) : base(message) { }

    public FitsException(string message, Exception innerException) : base(message, innerException) { }
}

namespace Eleris.FitsNet;

/// <summary>
/// Thrown by a save in <see cref="FitsSaveMode.CreateNew"/> mode when something already exists at the target path. Nothing was replaced and
/// no temporary file is left behind. It is an <see cref="IOException"/>; catch it by its type.
/// </summary>
public sealed class FitsFileExistsException : IOException
{
    /// <summary>Creates the exception for a path.</summary>
    /// <param name="fileName">The full path of the file that exists.</param>
    public FitsFileExistsException(string fileName) : this(fileName, null) { }

    /// <summary>Creates the exception for a path, with the exception that reported the problem.</summary>
    /// <param name="fileName">The full path of the file that exists.</param>
    /// <param name="innerException">The exception that reported the problem, or null.</param>
    public FitsFileExistsException(string fileName, Exception? innerException)
        : base($"The file '{fileName ?? throw new ArgumentNullException(nameof(fileName))}' already exists and the save mode does not replace it.", innerException)
    {
        FileName = fileName;
    }

    /// <summary>The full path of the file that exists.</summary>
    public string FileName { get; }
}

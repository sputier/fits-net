namespace Eleris.FitsNet;

/// <summary>What <see cref="FitsFile.Save(string, ChecksumPolicy, FitsSaveMode, CancellationToken)"/> does when a file already exists at the path.</summary>
public enum FitsSaveMode
{
    /// <summary>Replace an existing file, which keeps its attributes and permissions; create the file otherwise. The behaviour of the other <c>Save</c> overloads.</summary>
    Overwrite,

    /// <summary>
    /// Never replace anything: fail with <see cref="FitsFileExistsException"/> if something exists at the path, including when it appears
    /// while the file is being written.
    /// </summary>
    CreateNew,
}

/// <summary>
/// Thrown by a save in <see cref="FitsSaveMode.CreateNew"/> mode when something already exists at the target path. Nothing was replaced and
/// no temporary file is left behind. It is an <see cref="IOException"/>; catch it by its type.
/// </summary>
public sealed class FitsFileExistsException : IOException
{
    private const int ErrorFileExists = unchecked((int)0x80070050);

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
        HResult = ErrorFileExists;
    }

    /// <summary>The full path of the file that exists.</summary>
    public string FileName { get; }
}

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

using System.Buffers;

namespace Eleris.FitsNet.Tests;

/// <summary>
/// Memory whose every access runs an action: an image made of such planes (<see cref="ImageHdu.CreateFromPlanes{T}"/>) lets a test act
/// in the middle of a read or a save, at the exact moment a block of pixels is taken.
/// </summary>
internal sealed class HookedMemory<T>(T[] data, Action onAccess) : MemoryManager<T> where T : unmanaged
{
    public int Accesses { get; private set; }

    /// <summary>The memory, made without an access (<see cref="MemoryManager{T}.Memory"/> takes the span to learn its length).</summary>
    public ReadOnlyMemory<T> Plane => CreateMemory(data.Length);

    public override Span<T> GetSpan()
    {
        Accesses++;
        onAccess();
        return data;
    }

    public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

    public override void Unpin() { }

    protected override void Dispose(bool disposing) { }
}

/// <summary>A new empty directory, deleted with what it holds.</summary>
internal sealed class ScratchDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fitsnet-scratch-" + Guid.NewGuid().ToString("N"));

    public ScratchDirectory() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>The names of the files in the directory, sorted.</summary>
    public string[] Names() => Directory.GetFileSystemEntries(Path).Select(System.IO.Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal static class Planes
{
    /// <summary>Pixels of a cube of <paramref name="planes"/> planes, with values that differ from plane to plane.</summary>
    public static float[][] Float(int width, int height, int planes) =>
        Enumerable.Range(0, planes).Select(p => Enumerable.Range(0, width * height).Select(i => p * 1000f + i * 0.25f).ToArray()).ToArray();

    public static T[] Concat<T>(IEnumerable<T[]> planes) => planes.SelectMany(p => p).ToArray();

    public static IReadOnlyList<ReadOnlyMemory<T>> AsMemory<T>(IEnumerable<T[]> planes) => planes.Select(p => (ReadOnlyMemory<T>)p).ToList();

    public static byte[] Save(FitsFile file, ChecksumPolicy checksums = ChecksumPolicy.Auto)
    {
        using var stream = new MemoryStream();
        file.Save(stream, checksums);
        return stream.ToArray();
    }
}

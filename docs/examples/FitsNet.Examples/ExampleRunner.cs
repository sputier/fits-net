using System.Reflection;

namespace FitsNet.Examples;

/// <summary>Runs every example, in an order in which the files an example needs have been written by the ones before it.</summary>
public static class ExampleRunner
{
    private static readonly Type[] Pages =
    [
        typeof(StartExamples), typeof(FilesExamples), typeof(HeaderExamples), typeof(ImageExamples), typeof(TableExamples),
        typeof(CompressionExamples), typeof(ChecksumExamples), typeof(WcsExamples), typeof(TimeExamples), typeof(ConventionExamples),
        typeof(ReliabilityExamples),
    ];

    /// <summary>The names of all the examples, as <c>Class.Method</c>.</summary>
    public static IEnumerable<string> Names => Pages.SelectMany(t => Methods(t).Select(m => $"{t.Name}.{m.Name}"));

    private static IEnumerable<MethodInfo> Methods(Type page) =>
        page.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).OrderBy(m => m.MetadataToken);

    /// <summary>Runs all the examples in the current directory, after creating the sample files there.</summary>
    public static void RunAll(Action<string>? starting = null)
    {
        Samples.Prepare();
        foreach (var page in Pages)
            foreach (var method in Methods(page))
            {
                starting?.Invoke($"{page.Name}.{method.Name}");
                try
                {
                    method.Invoke(null, null);
                }
                catch (TargetInvocationException e) when (e.InnerException is not null)
                {
                    throw new InvalidOperationException($"{page.Name}.{method.Name} failed: {e.InnerException.Message}", e.InnerException);
                }
            }
    }
}

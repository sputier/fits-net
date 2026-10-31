using FitsNet.Examples;

namespace FitsNet.Tests;

[CollectionDefinition("Documentation examples", DisableParallelization = true)]
public sealed class DocumentationExamplesCollection;

/// <summary>The code samples of the guide (docs/examples) are real code: they all run.</summary>
[Collection("Documentation examples")]
public class DocumentationExamplesTests
{
    [Fact]
    public void Every_example_of_the_guide_runs()
    {
        var started = new List<string>();
        InScratchDirectory(() =>
        {
            ExampleRunner.RunAll(started.Add);
        });
        Assert.Equal(ExampleRunner.Names.Count(), started.Count);
        Assert.True(started.Count > 50, $"only {started.Count} examples ran");
    }

    [Fact]
    public void The_examples_print_what_the_guide_says()
    {
        string text = InScratchDirectory(() =>
        {
            Samples.Prepare();
            StartExamples.Inspect();
            ImageExamples.Unsigned();
            ImageExamples.Scaling();
            TimeExamples.Instants();
        });
        Assert.Contains("640 x 480 pixels, BITPIX 16", text);
        Assert.Contains("Target: M42, exposure 30 s", text);
        Assert.Contains("65535 is stored as 32767", text);
        Assert.Contains("60 from 100", text);
        Assert.Contains("2016-12-31T23:59:60.500", text);
    }

    /// <summary>Runs the action in a new empty directory, with the console captured; returns what it printed.</summary>
    private static string InScratchDirectory(Action action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "fitsnet-examples-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string previous = Directory.GetCurrentDirectory();
        var console = Console.Out;
        using var output = new StringWriter();
        try
        {
            Directory.SetCurrentDirectory(directory);
            Console.SetOut(output);
            action();
        }
        finally
        {
            Console.SetOut(console);
            Directory.SetCurrentDirectory(previous);
            DeleteWithRetries(directory);
        }
        return output.ToString();
    }

    // Another process (an indexer, an antivirus) can hold a freshly written file for a moment.
    private static void DeleteWithRetries(string directory)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt == 5) return;   // a leftover temporary folder is not a failure of the documentation
                Thread.Sleep(100 * attempt);
            }
        }
    }
}

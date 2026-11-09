namespace Eleris.FitsNet.Tests;

/// <summary><see cref="FitsSaveMode.CreateNew"/> never replaces a file, even one that appears while the save runs (issue #9).</summary>
public class CreateNewSaveTests
{
    private static FitsFile SmallFile() => new FitsFile().Add(ImageHdu.Create<short>([1, 2, 3, 4, 5, 6], 3, 2));

    [Fact]
    public void A_new_file_is_created_with_the_same_bytes_as_a_save_to_a_stream()
    {
        using var dir = new ScratchDirectory();
        var file = SmallFile();
        string path = dir.File("new.fits");

        file.Save(path, ChecksumPolicy.Always, FitsSaveMode.CreateNew);

        Assert.Equal(Planes.Save(file, ChecksumPolicy.Always), File.ReadAllBytes(path));
        Assert.Equal(["new.fits"], dir.Names());
    }

    [Fact]
    public void An_existing_file_is_refused_and_left_as_it_was()
    {
        using var dir = new ScratchDirectory();
        string path = dir.File("taken.fits");
        File.WriteAllText(path, "not mine");
        var file = SmallFile();
        var cards = file.Primary.Header.Select(c => c.ToString()).ToList();

        var e = Assert.Throws<FitsFileExistsException>(() => file.Save(path, ChecksumPolicy.Always, FitsSaveMode.CreateNew));

        Assert.Equal(Path.GetFullPath(path), e.FileName);
        Assert.IsAssignableFrom<IOException>(e);
        Assert.Contains(e.FileName, e.Message);
        Assert.Equal(new IOException().HResult, e.HResult);   // no error code of one platform
        Assert.Equal("not mine", File.ReadAllText(path));
        Assert.Equal(["taken.fits"], dir.Names());
        Assert.Equal(cards, file.Primary.Header.Select(c => c.ToString()));
    }

    [Fact]
    public void A_directory_at_the_path_counts_as_an_existing_file()
    {
        using var dir = new ScratchDirectory();
        string path = dir.File("folder.fits");
        Directory.CreateDirectory(path);

        Assert.Throws<FitsFileExistsException>(() => SmallFile().Save(path, ChecksumPolicy.Auto, FitsSaveMode.CreateNew));
        Assert.True(Directory.Exists(path));
        Assert.Equal(["folder.fits"], dir.Names());
    }

    [Theory]
    [InlineData(ChecksumPolicy.Auto)]
    [InlineData(ChecksumPolicy.Always)]
    public void A_file_that_appears_while_the_data_is_written_is_not_replaced(ChecksumPolicy checksums)
    {
        using var dir = new ScratchDirectory();
        string path = dir.File("race.fits");
        bool created = false;
        var plane = new HookedMemory<float>(new float[64], () =>
        {
            if (created) return;
            created = true;
            File.WriteAllText(path, "written by someone else"); // after the early check, before the file is put in place
        });
        var image = ImageHdu.CreateFromPlanes<float>([plane.Plane], 64, 1);
        var file = new FitsFile().Add(image);
        var cards = image.Header.Select(c => c.ToString()).ToList();

        var e = Assert.Throws<FitsFileExistsException>(() => file.Save(path, checksums, FitsSaveMode.CreateNew));

        Assert.True(created);
        Assert.IsAssignableFrom<IOException>(e.InnerException); // the refusal came from the move itself, not from the early check
        Assert.Equal("written by someone else", File.ReadAllText(path));
        Assert.Equal(["race.fits"], dir.Names());                       // no temporary file left
        Assert.Equal(cards, image.Header.Select(c => c.ToString()));    // the headers are rolled back

        // the file can still be saved elsewhere
        file.Save(dir.File("other.fits"), checksums, FitsSaveMode.CreateNew);
        Assert.Equal(Planes.Save(file, checksums), File.ReadAllBytes(dir.File("other.fits")));
    }

    [Fact]
    public void Concurrent_saves_in_CreateNew_mode_create_the_file_once()
    {
        using var dir = new ScratchDirectory();
        for (int round = 0; round < AsyncIoTests.ConcurrentRounds; round++)
        {
            string path = dir.File($"shared-{round}.fits");
            var files = AsyncIoTests.Contending();
            var outcome = new Exception?[files.Count];
            using var start = new Barrier(files.Count);   // all the saves start together, each on its own thread
            var threads = files.Select((f, k) => new Thread(() =>
            {
                Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(30)), "the threads did not all start");
                try
                {
                    f.Save(path, ChecksumPolicy.Always, FitsSaveMode.CreateNew);
                }
                catch (Exception e)
                {
                    outcome[k] = e;
                }
            })).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());
            AsyncIoTests.CheckOneWinner(files, outcome, path);
        }
        Assert.Equal(AsyncIoTests.ConcurrentRounds, dir.Names().Length);   // no temporary file and no claim left
    }

    [Fact]
    public void A_publication_that_fails_after_the_claim_leaves_nothing_at_the_target()
    {
        // The temporary file is missing, so the final move fails after the name was claimed (on Unix): the claim must be gone.
        using var dir = new ScratchDirectory();
        string target = dir.File("target.fits");
        Assert.ThrowsAny<IOException>(() => FitsFile.PublishNew(dir.File("missing.tmp"), target));
        Assert.Empty(dir.Names());
    }

    [Fact]
    public void A_claim_is_removed_only_while_it_is_still_ours()
    {
        using var dir = new ScratchDirectory();
        byte[] token = Guid.NewGuid().ToByteArray();
        string path = dir.File("claimed.fits");

        File.WriteAllBytes(path, token);
        FitsFile.RemoveClaim(path, token);
        Assert.False(File.Exists(path));                        // the whole claim

        File.WriteAllBytes(path, token[..5]);
        FitsFile.RemoveClaim(path, token);
        Assert.False(File.Exists(path));                        // a claim cut short by a failed write

        File.WriteAllBytes(path, []);
        FitsFile.RemoveClaim(path, token);
        Assert.False(File.Exists(path));                        // created, nothing written yet

        File.WriteAllBytes(path, Guid.NewGuid().ToByteArray());
        FitsFile.RemoveClaim(path, token);
        Assert.True(File.Exists(path));                         // someone else's 16 bytes

        File.WriteAllBytes(path, [.. token, 0]);
        FitsFile.RemoveClaim(path, token);
        Assert.True(File.Exists(path));                         // a longer file that starts like the claim

        FitsFile.RemoveClaim(dir.File("absent.fits"), token);   // nothing there: nothing to do
    }

    [Fact]
    public void The_file_an_instance_was_opened_from_is_never_replaced()
    {
        using var dir = new ScratchDirectory();
        string path = dir.File("source.fits");
        SmallFile().Save(path);
        byte[] before = File.ReadAllBytes(path);

        using var file = FitsFile.Open(path);
        file.Primary.Header.Set("OBSERVER", "someone");
        Assert.Throws<FitsFileExistsException>(() => file.Save(path, ChecksumPolicy.Auto, FitsSaveMode.CreateNew));

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal([1, 2, 3, 4, 5, 6], ((ImageHdu)file.Primary).ReadPixels<short>());   // still reads its source

        // saved somewhere new, the HDUs keep reading the source they came from
        file.Save(dir.File("copy.fits"), ChecksumPolicy.Auto, FitsSaveMode.CreateNew);
        Assert.Equal([1, 2, 3, 4, 5, 6], ((ImageHdu)file.Primary).ReadPixels<short>());
        using var copy = FitsFile.Open(dir.File("copy.fits"));
        Assert.Equal("someone", copy.Primary.Header.Get<string>("OBSERVER"));
    }

    [Fact]
    public void Overwrite_mode_replaces_like_the_other_overloads()
    {
        using var dir = new ScratchDirectory();
        string path = dir.File("a.fits");
        File.WriteAllText(path, "old");
        var file = SmallFile();

        file.Save(path, ChecksumPolicy.Auto, FitsSaveMode.Overwrite);
        Assert.Equal(Planes.Save(file), File.ReadAllBytes(path));

        File.WriteAllText(path, "old");
        file.Save(path, ChecksumPolicy.Auto, CancellationToken.None);
        Assert.Equal(Planes.Save(file), File.ReadAllBytes(path));
    }

    [Fact]
    public void An_unknown_mode_is_refused_before_anything_is_written()
    {
        using var dir = new ScratchDirectory();
        Assert.Throws<ArgumentOutOfRangeException>(() => SmallFile().Save(dir.File("x.fits"), ChecksumPolicy.Auto, (FitsSaveMode)7));
        Assert.Empty(dir.Names());
    }

    [Fact]
    public void A_symbolic_link_at_the_path_counts_as_an_existing_file()
    {
        using var dir = new ScratchDirectory();
        string link = dir.File("link.fits");
        try
        {
            File.CreateSymbolicLink(link, dir.File("missing-target.fits")); // a broken link
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return; // creating links needs a privilege on Windows: nothing to check on this machine
        }

        Assert.Throws<FitsFileExistsException>(() => SmallFile().Save(link, ChecksumPolicy.Auto, FitsSaveMode.CreateNew));
        Assert.False(File.Exists(dir.File("missing-target.fits")));
        Assert.Equal(["link.fits"], dir.Names());
    }
}

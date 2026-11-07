namespace BreakMeIfYouCan.Tests.Unit;

internal sealed class TestTempDirectory : IDisposable
{
    public TestTempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bm-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Write(string name, string content)
    {
        var file = System.IO.Path.Combine(Path, name);
        var directory = System.IO.Path.GetDirectoryName(file);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(file, content);
        return file;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A failed cleanup must not fail a test.
        }
    }
}

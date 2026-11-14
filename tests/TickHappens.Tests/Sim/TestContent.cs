using System.Text.Json;
using System.Text.Json.Nodes;
using TickHappens.Game.Content;

namespace TickHappens.Tests;

/// <summary>
/// Locates the core content next to the repository and builds throwaway content directories
/// for the validation tests. Scratch directories live under the repository's .tmp/tests/content.
/// </summary>
internal static class TestContent
{
    public static string CoreDirectory { get; } = ContentLoader.FindCore();
    public static string ScratchRoot => Path.Combine(AssetPipelineTests.TmpDir(), "content");

    public static ContentSet LoadCore() => ContentLoader.Load(CoreDirectory);

    /// <summary>Copies every core content file into a fresh scratch directory and returns it.</summary>
    public static string CreateScratchCopy()
    {
        string dir = Path.Combine(ScratchRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (string file in Directory.GetFiles(CoreDirectory))
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
        return dir;
    }

    /// <summary>Parses a JSON file of the copy, lets the caller edit it, and writes it back.</summary>
    public static void EditJson(string dir, string file, Action<JsonNode> edit)
    {
        string path = Path.Combine(dir, file);
        JsonNode root = JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidOperationException($"{file} is empty");
        edit(root);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Copies core, breaks it with <paramref name="breakContent"/>, loads it and returns the validation errors.</summary>
    public static IReadOnlyList<string> LoadErrors(Action<string> breakContent)
    {
        string dir = CreateScratchCopy();
        try
        {
            breakContent(dir);
            var error = Assert.Throws<ContentException>(() => ContentLoader.Load(dir));
            Assert.NotEmpty(error.Errors);
            return error.Errors;
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

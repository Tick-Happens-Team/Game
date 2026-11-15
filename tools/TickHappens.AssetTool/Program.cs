using TickHappens.AssetTool;
using TickHappens.AssetTool.Audio;
using TickHappens.AssetTool.ProcGen;
using TickHappens.Engine.Assets;

// TickHappens asset pipeline. Run from the repository root:
//   dotnet run --project tools/TickHappens.AssetTool -- build
//   dotnet run --project tools/TickHappens.AssetTool -- gen <recipe.json> [--seeds A-B]
//   dotnet run --project tools/TickHappens.AssetTool -- import <raw.png> <name> <frameW> <frameH> [--tool NAME] [--normal raw_n.png]
//   dotnet run --project tools/TickHappens.AssetTool -- lint <file.png> [frameW frameH]
//   dotnet run --project tools/TickHappens.AssetTool -- snap <in.png> <out.png>
//   dotnet run --project tools/TickHappens.AssetTool -- audio [NAME]   (sound and music recipes -> assets/audio WAV)

string assetsRoot = AssetPaths.SourceRoot(Directory.GetCurrentDirectory());
string palettePath = Path.Combine(assetsRoot, "art/palette/tickhappens-v1.hex");
string sourceDir = Path.Combine(assetsRoot, "art/source");
string importDir = Path.Combine(sourceDir, "imported");
string recipeDir = Path.Combine(assetsRoot, "art/recipes");
string atlasOut = Path.Combine(assetsRoot, "assets/atlas/main");
const string PreviewDir = ".tmp/preview";

if (!File.Exists(palettePath))
{
    Console.Error.WriteLine($"Palette not found at {palettePath}. Run from Game with Assets beside it, or set TICKHAPPENS_ASSETS_ROOT.");
    return 2;
}
var pal = Palette.Load(palettePath);
string cmd = args.Length > 0 ? args[0] : "build";

switch (cmd)
{
    case "build":
    {
        var sheets = new List<SpriteSheet>();
        foreach (var f in Directory.EnumerateFiles(sourceDir, "*.pxs", SearchOption.AllDirectories).Order())
            sheets.Add(PxsCompiler.Compile(f, pal));
        if (Directory.Exists(recipeDir))
            foreach (var f in Directory.EnumerateFiles(recipeDir, "*.json").Order())
                sheets.Add(new RecipeRenderer(Recipe.Load(f), pal).Render(f));
        if (Directory.Exists(importDir))
            foreach (var f in Directory.EnumerateFiles(importDir, "*.sprite.json").Order())
                sheets.Add(SpriteImport.Load(f));
        sheets.AddRange(TileGenerator.Generate(pal));

        var dup = sheets.GroupBy(s => s.Name).FirstOrDefault(g => g.Count() > 1);
        if (dup != null) { Console.Error.WriteLine($"duplicate sprite name '{dup.Key}'"); return 1; }

        var issues = sheets.SelectMany(s => StyleLint.Check(s, pal)).ToList();
        foreach (var i in issues) Console.WriteLine(i);
        if (issues.Any(i => i.Severity == Severity.Error)) { Console.Error.WriteLine("style lint failed"); return 1; }

        var atlas = AtlasBuilder.Build(sheets, atlasOut);
        Preview.Write(sheets, PreviewDir);
        Console.WriteLine($"atlas {atlas.Size[0]}x{atlas.Size[1]}: {sheets.Count} sprites, {sheets.Sum(s => s.Frames.Count)} frames, {issues.Count} warnings -> {atlasOut}.png/_e.png/_n.png/.json");
        Console.WriteLine($"previews -> {PreviewDir}/");
        AudioBuilder.Build(assetsRoot, PreviewDir);
        return 0;
    }
    case "gen":
    {
        // Renders one recipe for a range of seeds into a contact sheet for picking a variant.
        var recipe = Recipe.Load(args[1]);
        int s0 = recipe.Seed, s1 = recipe.Seed;
        for (int i = 2; i < args.Length; i++)
            if (args[i] == "--seeds") { var r = args[++i].Split('-'); s0 = int.Parse(r[0]); s1 = int.Parse(r[^1]); }
        var rows = new List<SpriteSheet>();
        for (int seed = s0; seed <= s1; seed++)
        {
            var sheet = new RecipeRenderer(recipe, pal, seed).Render(args[1]);
            foreach (var issue in StyleLint.Check(sheet, pal)) Console.WriteLine($"seed {seed}: {issue}");
            rows.Add(sheet);
        }
        string outPath = Path.Combine(".tmp", "gen", $"{recipe.Sprite}_seeds_{s0}-{s1}.png");
        Preview.ContactSheet(rows, outPath, 6);
        Console.WriteLine($"{rows.Count} variants x {rows[0].Frames.Count} frames -> {outPath}");
        return 0;
    }
    case "import":
    {
        if (args.Length < 5) { Console.Error.WriteLine("usage: import <raw.png> <name> <frameW> <frameH> [--asset-id ID] [--prompt TEXT] [--normal PATH] [--ref PATH]"); return 2; }
        var prov = new Provenance();
        string? normal = null;
        for (int i = 5; i < args.Length; i++)
            switch (args[i])
            {
                case "--asset-id": prov.AssetId = args[++i]; break;
                case "--prompt": prov.Prompt = args[++i]; break;
                case "--normal": normal = args[++i]; break;
                case "--ref": prov.References.Add(args[++i]); break;
                case "--tool": prov.Tool = args[++i]; break;
                default: Console.Error.WriteLine($"unknown option {args[i]}"); return 2;
            }
        var m = SpriteImport.Import(args[1], args[2], int.Parse(args[3]), int.Parse(args[4]), pal, importDir, prov, normal);
        Console.WriteLine($"imported {m.Name} (source scale x{m.Provenance.ImportedScale}) -> {importDir}/{m.Image}");
        var issues = StyleLint.Check(SpriteImport.Load(Path.Combine(importDir, m.Name + ".sprite.json")), pal);
        foreach (var i in issues) Console.WriteLine(i);
        return issues.Any(i => i.Severity == Severity.Error) ? 1 : 0;
    }
    case "lint":
    {
        var img = RgbaImage.Load(args[1]);
        int fw = args.Length > 3 ? int.Parse(args[2]) : img.W, fh = args.Length > 3 ? int.Parse(args[3]) : img.H;
        var sheet = new SpriteSheet { Name = Path.GetFileNameWithoutExtension(args[1]), W = fw, H = fh, Source = args[1] };
        for (int y = 0; y + fh <= img.H; y += fh)
            for (int x = 0; x + fw <= img.W; x += fw)
                sheet.Frames.Add(new SpriteFrame($"{x / fw},{y / fh}", img.Crop(x, y, fw, fh), new RgbaImage(fw, fh)));
        var issues = StyleLint.Check(sheet, pal);
        foreach (var i in issues) Console.WriteLine(i);
        Console.WriteLine($"pixel scale x{PixelScale.Detect(img)}, {issues.Count} issues");
        return issues.Any(i => i.Severity == Severity.Error) ? 1 : 0;
    }
    case "snap":
    {
        var img = RgbaImage.Load(args[1]);
        for (int i = 0; i < img.Px.Length; i++)
            img.Px[i] = RgbaImage.A(img.Px[i]) < 128 ? 0 : pal.Nearest(img.Px[i]) | 0xFF000000;
        img.Save(args[2]);
        return 0;
    }
    case "audio":
        AudioBuilder.Build(assetsRoot, PreviewDir, args.Length > 1 ? args[1] : null);
        return 0;
    case "crop":
    {
        // crop <in.png> <x> <y> <w> <h> <zoom> <out.png> - nearest-neighbour enlargement for visual QA
        var img = RgbaImage.Load(args[1]);
        int x0 = int.Parse(args[2]), y0 = int.Parse(args[3]), cw = int.Parse(args[4]), ch = int.Parse(args[5]), z = int.Parse(args[6]);
        var src = img.Crop(x0, y0, cw, ch);
        var dst = new RgbaImage(cw * z, ch * z);
        for (int y = 0; y < dst.H; y++)
            for (int x = 0; x < dst.W; x++) dst[x, y] = src[x / z, y / z];
        dst.Save(args[7]);
        return 0;
    }
    default:
        Console.Error.WriteLine($"unknown command '{cmd}' (build | gen | import | lint | snap | audio | crop)");
        return 2;
}

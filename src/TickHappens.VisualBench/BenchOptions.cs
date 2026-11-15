namespace TickHappens.VisualBench;

public sealed class BenchOptions
{
    public int Enemies = 1500;
    public int Frames;
    public string? Screenshot;
    public bool NoVsync;
    public bool Demo;
    public string? Effect;
    public bool AutoCast = true;
    public string Art = "recipe";
    public string Report = Path.Combine(".tmp", "bench", "frames.csv");

    public static BenchOptions Parse(string[] args)
    {
        var o = new BenchOptions();
        for (int i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--enemies": o.Enemies = int.Parse(args[++i]); break;
                case "--frames": o.Frames = int.Parse(args[++i]); break;
                case "--screenshot": o.Screenshot = args[++i]; break;
                case "--no-vsync": o.NoVsync = true; break;
                case "--demo": o.Demo = true; break;
                case "--effect": o.Effect = args[++i]; break;
                case "--manual": o.AutoCast = false; break;
                case "--art": o.Art = args[++i]; break;
                case "--report": o.Report = args[++i]; break;
                default: throw new ArgumentException($"unknown option {args[i]}");
            }
        return o;
    }
}

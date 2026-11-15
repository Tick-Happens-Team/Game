using System.Globalization;
using TickHappens.Engine.Ui;
using TickHappens.Engine.Vfx;
using TickHappens.Game.Sim;
using TickHappens.Localization;

namespace TickHappens.Game;

internal sealed partial class Hud
{
    private const int HeaderTop = 8, HeaderHeight = 30, HeaderMargin = 6;
    internal const int RunHeaderBottom = HeaderTop + HeaderHeight;

    private const uint HeaderFace = 0xFF2C1D23u, HeaderEdge = 0xFF594254u, HeaderShadow = 0xFF170F17u;
    private const uint Rose = 0xFF6654F0u, RoseLight = 0xFFB2AEFFu, Brass = 0xFF468DD2u, GoldLight = 0xFF9FECFFu;
    private const uint Bone = 0xFFC5BDDEu;

    // Each row is nine pixels wide. '#' is the base color and '+' is the highlight.
    private const string HeartIcon =
        ".++..++.." +
        "+++++##+." +
        "+#+####+." +
        "########." +
        ".######.." +
        "..####..." +
        "...##....";
    private const string BladeIcon =
        "......++." +
        ".....+#+." +
        "....+#+.." +
        ".#.+#+..." +
        "..##+...." +
        "..##....." +
        ".#..#....";
    private const string BootIcon =
        ".++++...." +
        ".+###...." +
        ".+###...." +
        ".+####+.." +
        ".+#+++++." +
        ".+######+" +
        ".########";
    private const string TempoIcon =
        ".....++.." +
        "....+#..." +
        "...+#...." +
        "..+####.." +
        "....#+..." +
        "...#+...." +
        "..#+.....";
    private const string ShieldIcon =
        ".+++++++." +
        ".+#++##+." +
        ".+#++##+." +
        "..#++##.." +
        "..#++##.." +
        "...+##..." +
        "....#....";
    private const string WardIcon =
        "....+...." +
        "...+#+..." +
        "..+#++#.." +
        ".+##+##+." +
        "..#++##.." +
        "...+##..." +
        "....#....";
    private const string ClockIcon =
        "..+++++.." +
        ".+#...#+." +
        "+...#...#" +
        "+...#+..#" +
        "+.......#" +
        ".#.....#." +
        "..#####..";
    private const string HourglassIcon =
        ".+++++++." +
        "..#...#.." +
        "...#+#..." +
        "....#...." +
        "...#+#..." +
        "..#+++#.." +
        ".#######.";
    // Counter sprites are 15 by 13 pixels. The letters index each sprite's palette.
    private const string CoinIcon =
        ".....ooooo....." +
        "...oollllyoo..." +
        "..olyyyyyyso..." +
        ".olyyssssyyso.." +
        ".lyysyyyyssyo.." +
        "olyysyylyssyso." +
        "olysyyllyssyso." +
        "olysyylyyssyso." +
        "oyyssylysysyso." +
        ".syysssssyyso.." +
        ".osyyyyyyysoo.." +
        "..oosssssoo...." +
        "....ooooo......";
    private const string ChestIcon =
        "....ooooooo...." +
        "...oslllllso..." +
        "..osylsslyyso.." +
        ".osyylsslyyso.." +
        ".ollllsslllso.." +
        "ooooooyyooooooo" +
        "olyyyyllyyyyuso" +
        "oswwwslllswwwso" +
        "oswwssyysswwsso" +
        "oswwssoosswwsso" +
        "osuuwwwwwwwusso" +
        "olsuuuuuuuussso" +
        ".ooooooooooooo.";
    private const string SkullIcon =
        "....ooooooo...." +
        "...ollllllso..." +
        "..ollyyyyyyso.." +
        ".ollyyyyyyyyso." +
        ".olyyssyyssyso." +
        ".oyysooysooyso." +
        ".oyyoooysooyso." +
        "..oyssyoyssso.." +
        "...oyyoooyso..." +
        "...osysysyo...." +
        "...oyoyoyso...." +
        "....osssso....." +
        ".....oooo......";

    private static readonly uint[] CoinPalette = [0xFF241327, 0xFFB2F5FF, 0xFF59CFFF, 0xFF2879C2];
    private static readonly uint[] ChestPalette = [0xFF191224, 0xFFB0EBFF, 0xFF67B3F3, 0xFF456A9D, 0xFF214574, 0xFF345B91];
    private static readonly uint[] SkullPalette = [0xFF241327, 0xFFF4F2FF, 0xFFCFCADFu, 0xFF9A819Fu];
    private const string LootIcon =
        "....+...." +
        "...+##..." +
        "..+####.." +
        "...###..." +
        "....#....";

    private static void DrawRunHeader(PixelCanvas ui, RunWorld world, int width)
    {
        var player = world.Player;
        int seconds = (int)world.RunSeconds, bonus = (int)world.BonusSeconds;
        float maxHealth = world.MaxHealth;
        var culture = CultureInfo.InvariantCulture;
        Span<char> timeBuffer = stackalloc char[16], bonusBuffer = stackalloc char[16], hpBuffer = stackalloc char[24];
        timeBuffer.TryWrite(culture, $"{seconds / 60}:{seconds % 60:00}", out int timeLength);
        bonusBuffer.TryWrite(culture, $"+{bonus / 60}:{bonus % 60:00}", out int bonusLength);
        hpBuffer.TryWrite(culture, $"{(int)MathF.Ceiling(player.Hp)}/{(int)MathF.Ceiling(maxHealth)}", out int hpLength);
        var time = timeBuffer[..timeLength];
        var hp = hpBuffer[..hpLength];

        Bar(ui, HeaderMargin + 1, 2, width - HeaderMargin * 2 - 2, 3,
            player.Xp / MathF.Max(1, player.XpNext), Teal, HeaderEdge);

        // Size counters for their contents and reserve the middle for the clock.
        int clockWidth = Math.Max(102, PixelFont.Measure(time, 2) + 29);
        int clockX = (width - clockWidth) / 2;
        HeaderPlate(ui, clockX, HeaderTop, clockWidth, HeaderHeight, Brass);
        int clockY = bonus > 0 ? HeaderTop + 3 : HeaderTop + 8;
        int timeX = clockX + (clockWidth - PixelFont.Measure(time, 2) + 13) / 2;
        DrawIcon(ui, timeX - 14, clockY + 3, ClockIcon, Brass, GoldLight);
        PixelFont.Draw(ui, time, timeX, clockY, White, Ink, 2);
        if (bonus > 0)
        {
            var extra = bonusBuffer[..bonusLength];
            int bonusX = clockX + (clockWidth - PixelFont.Measure(extra) + 13) / 2;
            DrawIcon(ui, bonusX - 14, HeaderTop + 21, HourglassIcon, TealDark, Teal);
            PixelFont.Draw(ui, extra, bonusX, HeaderTop + 21, Teal, Ink);
        }

        string hero = Localizer.Format("hud.level", CharacterName(player.Character), player.Level);
        string souls = player.Character.Id == "necromancer" ? Localizer.Format("hud.souls", player.Souls) : "";
        int soulsWidth = souls.Length > 0 ? PixelFont.Measure(souls) + 8 : 0;
        int available = clockX - HeaderMargin * 2;
        int healthWidth = Math.Min(available, Math.Max(164, PixelFont.Measure(hero) + 12 + soulsWidth));
        HeaderPlate(ui, HeaderMargin, HeaderTop, healthWidth, HeaderHeight, Rose);
        int maxChars = Math.Max(0, (healthWidth - 12 - soulsWidth) / PixelFont.Advance);
        PixelFont.Draw(ui, hero.AsSpan(0, Math.Min(hero.Length, maxChars)), HeaderMargin + 6, HeaderTop + 5, White, Ink);
        if (souls.Length > 0)
            PixelFont.Draw(ui, souls, HeaderMargin + healthWidth - soulsWidth + 2, HeaderTop + 5, Teal, Ink);

        int meterX = HeaderMargin + 19, meterY = HeaderTop + 16, meterWidth = healthWidth - 25;
        DrawIcon(ui, HeaderMargin + 5, meterY + 2, HeartIcon, Rose, RoseLight);
        Bar(ui, meterX, meterY, meterWidth, 10, player.Hp / MathF.Max(1, maxHealth), Red, RedDark);
        float shield = player.Shield + player.BonusShield;
        if (shield > 0)
            Bar(ui, meterX, meterY + 11, meterWidth, 1, shield / MathF.Max(1, maxHealth), Teal, TealDark);
        PixelFont.Draw(ui, hp, meterX + (meterWidth - PixelFont.Measure(hp)) / 2, meterY + 1, White, Ink);

        int right = width - HeaderMargin;
        right = DrawCounter(ui, right, player.Kills, SkullIcon, SkullPalette, Bone, White);
        right = DrawCounter(ui, right, player.Wallet.NextPrice, ChestIcon, ChestPalette, Brass, GoldLight);
        DrawCounter(ui, right, player.Wallet.Gold, CoinIcon, CoinPalette, Gold, GoldLight);
    }

    private static int DrawCounter(PixelCanvas ui, int right, int value, string icon, ReadOnlySpan<uint> palette, uint color, uint highlight)
    {
        Span<char> buffer = stackalloc char[11];
        value.TryFormat(buffer, out int length, provider: CultureInfo.InvariantCulture);
        var number = buffer[..length];
        int textWidth = PixelFont.Measure(number);
        int width = Math.Max(49, textWidth + 30);
        int x = right - width;
        int textX = x + (width - textWidth + 19) / 2;
        HeaderPlate(ui, x, HeaderTop, width, HeaderHeight, color);
        const int iconWidth = 15;
        for (int i = 0; i < icon.Length; i++)
        {
            int shade = "olyswu".IndexOf(icon[i]);
            if (shade >= 0)
                ui.Set(textX - 19 + i % iconWidth, HeaderTop + 8 + i / iconWidth, palette[shade]);
        }
        PixelFont.Draw(ui, number, textX, HeaderTop + 11, highlight, Ink);
        return x - 4;
    }

    private static void HeaderPlate(PixelCanvas ui, int x, int y, int width, int height, uint accent)
    {
        for (int row = 0; row < height; row++)
        {
            int inset = row == 0 || row == height - 1 ? 2 : 0;
            for (int col = inset; col < width - inset; col++)
            {
                uint color = HeaderFace;
                if (row == 0 || col == 0 || col == width - 1) color = HeaderEdge;
                if (row >= height - 2) color = HeaderShadow;
                ui.Set(x + col, y + row, color);
            }
        }
        int center = x + width / 2;
        ui.Line(center - 7, y, center + 7, y, accent);
    }

    private static void DrawIcon(PixelCanvas ui, int x, int y, string pixels, uint color, uint highlight)
    {
        const int width = 9;
        for (int i = 0; i < pixels.Length; i++)
            switch (pixels[i])
            {
                case '#': ui.Set(x + i % width, y + i / width, color); break;
                case '+': ui.Set(x + i % width, y + i / width, highlight); break;
            }
    }
}

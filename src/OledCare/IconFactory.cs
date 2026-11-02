using System.Drawing.Drawing2D;

namespace OledCare;

/// <summary>Duruma göre renk değiştiren tepsi simgelerini çizer (ayrı ikon dosyası gerekmez).</summary>
internal static class IconFactory
{
    private static readonly Dictionary<EngineState, Icon> Cache = new();

    public static Color ColorFor(EngineState state) => state switch
    {
        EngineState.Active => Color.FromArgb(46, 160, 67),    // yeşil: kullanılıyor
        EngineState.Watching => Color.FromArgb(230, 162, 26), // turuncu: kamera açık, kontrol ediliyor
        EngineState.Away => Color.FromArgb(56, 132, 230),     // mavi: ekran kapalı
        EngineState.Paused => Color.FromArgb(150, 150, 150),  // gri: beklemede
        _ => Color.FromArgb(200, 60, 60),                     // kırmızı: durduruldu
    };

    public static Icon For(EngineState state)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(state, out var icon))
            {
                icon = Draw(ColorFor(state));
                Cache[state] = icon;
            }

            return icon;
        }
    }

    private static Icon Draw(Color dot)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Monitör gövdesi ve ayağı
            using var frame = new SolidBrush(Color.FromArgb(235, 235, 235));
            using var screen = new SolidBrush(Color.FromArgb(20, 20, 20));
            g.FillRectangle(frame, 2, 4, 28, 19);
            g.FillRectangle(screen, 4, 6, 24, 15);
            g.FillRectangle(frame, 13, 23, 6, 3);
            g.FillRectangle(frame, 9, 26, 14, 2);

            // Durum noktası
            using var dotBrush = new SolidBrush(dot);
            using var outline = new Pen(Color.White, 1.5f);
            g.FillEllipse(dotBrush, 17, 15, 14, 14);
            g.DrawEllipse(outline, 17, 15, 14, 14);
        }

        var handle = bmp.GetHicon();
        try
        {
            // Icon.FromHandle tanıtıcının sahibi olmaz; kopyalayıp asıl tanıtıcıyı serbest bırakıyoruz.
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}

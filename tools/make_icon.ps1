param([string]$Variant = "", [int]$Size = 256, [string]$Out = "")
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System.Drawing;
using System.Drawing.Drawing2D;
public static class IconArt
{
    static GraphicsPath Pill(float x, float y, float w, float h)
    {
        var p = new GraphicsPath();
        p.AddArc(x, y, h, h, 90, 180);
        p.AddArc(x + w - h, y, h, h, 270, 180);
        p.CloseFigure();
        return p;
    }
    static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        p.AddArc(x, y, r * 2, r * 2, 180, 90);
        p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        p.CloseFigure();
        return p;
    }
    static void Glow(Graphics g, float cx, float cy, float r, Color c)
    {
        using (var gp = new GraphicsPath())
        {
            gp.AddEllipse(cx - r, cy - r, r * 2, r * 2);
            using (var b = new PathGradientBrush(gp))
            {
                b.CenterColor = c;
                b.SurroundColors = new[] { Color.FromArgb(0, c) };
                g.FillPath(b, gp);
            }
        }
    }
    static void Dot(Graphics g, float x, float y, float d, Color c, bool glow)
    {
        if (glow) Glow(g, x + d / 2, y + d / 2, d * 1.25f, Color.FromArgb(120, c));
        using (var b = new SolidBrush(c)) g.FillEllipse(b, x, y, d, d);
        using (var hl = new SolidBrush(Color.FromArgb(110, 255, 255, 255))) g.FillEllipse(hl, x + d * 0.22f, y + d * 0.14f, d * 0.4f, d * 0.3f);
    }

    public static Bitmap Draw(string v, int s)
    {
        var bmp = new Bitmap(s, s);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float S = s;
            if (v == "A")
            {
                using (var tile = RoundRect(S * 0.06f, S * 0.06f, S * 0.88f, S * 0.88f, S * 0.2f))
                using (var bg = new LinearGradientBrush(new PointF(0, 0), new PointF(S, S), Color.FromArgb(255, 96, 76, 220), Color.FromArgb(255, 36, 150, 230)))
                    g.FillPath(bg, tile);
                Glow(g, S * 0.3f, S * 0.25f, S * 0.35f, Color.FromArgb(90, 255, 120, 220));
                float w = S * 0.66f, h = S * 0.3f, x = (S - w) / 2, y = (S - h) / 2;
                using (var pill = Pill(x, y, w, h))
                {
                    using (var fill = new LinearGradientBrush(new PointF(0, y), new PointF(0, y + h), Color.FromArgb(150, 255, 255, 255), Color.FromArgb(70, 255, 255, 255)))
                        g.FillPath(fill, pill);
                    using (var pen = new Pen(Color.FromArgb(200, 255, 255, 255), S / 90f)) g.DrawPath(pen, pill);
                }
                float d = h * 0.38f;
                Dot(g, x + w - h * 0.5f - d * 0.6f, y + (h - d) / 2, d, Color.FromArgb(255, 48, 209, 88), true);
            }
            else if (v == "B")
            {
                float w = S * 0.92f, h = S * 0.48f, x = (S - w) / 2, y = (S - h) / 2;
                using (var pill = Pill(x, y, w, h))
                {
                    using (var fill = new LinearGradientBrush(new PointF(0, y), new PointF(0, y + h), Color.FromArgb(255, 44, 44, 50), Color.FromArgb(255, 6, 6, 8)))
                        g.FillPath(fill, pill);
                    using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255), S / 100f)) g.DrawPath(pen, pill);
                }
                using (var shine = Pill(x + h * 0.25f, y + h * 0.08f, w - h * 0.5f, h * 0.38f))
                using (var sb = new LinearGradientBrush(new PointF(0, y), new PointF(0, y + h * 0.5f), Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)))
                    g.FillPath(sb, shine);
                float d = h * 0.34f;
                Dot(g, x + w - h * 0.5f - d * 0.65f, y + (h - d) / 2, d, Color.FromArgb(255, 48, 209, 88), true);
            }
            else if (v == "C")
            {
                float w = S * 0.92f, h = S * 0.48f, x = (S - w) / 2, y = (S - h) / 2;
                using (var pill = Pill(x, y, w, h))
                {
                    using (var fill = new LinearGradientBrush(new PointF(x, y), new PointF(x + w, y + h), Color.FromArgb(235, 210, 214, 230), Color.FromArgb(235, 120, 126, 150)))
                        g.FillPath(fill, pill);
                    using (var pen = new Pen(Color.FromArgb(230, 255, 255, 255), S / 80f)) g.DrawPath(pen, pill);
                }
                using (var shine = Pill(x + h * 0.25f, y + h * 0.07f, w - h * 0.5f, h * 0.4f))
                using (var sb = new LinearGradientBrush(new PointF(0, y), new PointF(0, y + h * 0.5f), Color.FromArgb(150, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)))
                    g.FillPath(sb, shine);
                float d = h * 0.34f;
                Dot(g, x + w - h * 0.5f - d * 0.65f, y + (h - d) / 2, d, Color.FromArgb(255, 30, 190, 80), true);
                using (var bar = Pill(x + h * 0.45f, y + h * 0.44f, w * 0.36f, h * 0.12f))
                using (var bb = new SolidBrush(Color.FromArgb(200, 40, 44, 60))) g.FillPath(bb, bar);
            }
            else if (v == "D")
            {
                using (var tile = RoundRect(S * 0.06f, S * 0.06f, S * 0.88f, S * 0.88f, S * 0.2f))
                using (var bg = new LinearGradientBrush(new PointF(0, 0), new PointF(0, S), Color.FromArgb(255, 34, 34, 40), Color.FromArgb(255, 10, 10, 12)))
                {
                    g.FillPath(bg, tile);
                    using (var pen = new Pen(Color.FromArgb(60, 255, 255, 255), S / 120f)) g.DrawPath(pen, tile);
                }
                float w = S * 0.66f, h = S * 0.28f, x = (S - w) / 2, y = S * 0.2f;
                using (var clip = RoundRect(S * 0.06f, S * 0.06f, S * 0.88f, S * 0.88f, S * 0.2f))
                {
                    g.SetClip(clip);
                    Glow(g, S / 2, y + h / 2, w * 0.6f, Color.FromArgb(70, 48, 209, 88));
                    g.ResetClip();
                }
                using (var pill = Pill(x, y, w, h))
                {
                    using (var fill = new SolidBrush(Color.FromArgb(255, 0, 0, 0))) g.FillPath(fill, pill);
                    using (var pen = new Pen(Color.FromArgb(150, 255, 255, 255), S / 110f)) g.DrawPath(pen, pill);
                }
                float d = h * 0.36f;
                Dot(g, x + w - h * 0.5f - d * 0.6f, y + (h - d) / 2, d, Color.FromArgb(255, 48, 209, 88), true);
                using (var lines = new SolidBrush(Color.FromArgb(90, 255, 255, 255)))
                {
                    using (var l1 = Pill(S * 0.2f, S * 0.6f, S * 0.6f, S * 0.07f)) g.FillPath(lines, l1);
                    using (var l2 = Pill(S * 0.2f, S * 0.72f, S * 0.4f, S * 0.07f)) g.FillPath(lines, l2);
                }
            }
        }
        return bmp;
    }
}
'@ -ReferencedAssemblies System.Drawing

if ($Variant -eq "") {
    $sheet = New-Object System.Drawing.Bitmap 1120, 330
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 32, 32, 36))
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 235, 235, 240))), 0, 280, 1120, 50)
    $font = New-Object System.Drawing.Font("Segoe UI", 20, [System.Drawing.FontStyle]::Bold)
    $i = 0
    foreach ($v in "A","B","C","D") {
        $img = [IconArt]::Draw($v, 256)
        $g.DrawImage($img, 10 + $i * 280, 10)
        $small = [IconArt]::Draw($v, 32)
        $g.DrawImage($small, 10 + $i * 280 + 160, 289)
        $s16 = [IconArt]::Draw($v, 16)
        $g.DrawImage($s16, 10 + $i * 280 + 210, 297)
        $g.DrawString($v, $font, [System.Drawing.Brushes]::Black, 20 + $i * 280, 286)
        $i++
    }
    $sheet.Save($Out)
} else {
    $sizes = 16, 24, 32, 48, 64, 128, 256
    $pngs = @()
    foreach ($s in $sizes) {
        $b = [IconArt]::Draw($Variant, $s)
        $ms = New-Object System.IO.MemoryStream
        $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngs += , $ms.ToArray()
    }
    $fs = [System.IO.File]::Create($Out)
    $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $bb = if ($s -ge 256) { 0 } else { $s }
        $bw.Write([byte]$bb); $bw.Write([byte]$bb); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([UInt16]1); $bw.Write([UInt16]32)
        $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $bw.Write($p) }
    $bw.Close()
}

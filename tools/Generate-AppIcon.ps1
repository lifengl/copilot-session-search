param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\CopilotSessionSearch\Assets')
)

Add-Type -AssemblyName System.Drawing

$source = @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class AppIconGenerator
{
    public static void Generate(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        using var source = new Bitmap(1024, 1024, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(source))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using GraphicsPath bubble = CreateRoundedRectangle(
                new RectangleF(86, 82, 670, 620),
                148);
            using var bubbleBrush = new LinearGradientBrush(
                new PointF(110, 100),
                new PointF(740, 700),
                Color.FromArgb(255, 126, 72, 210),
                Color.FromArgb(255, 24, 130, 218));
            using var bubbleOutline = new Pen(Color.FromArgb(255, 19, 37, 61), 42)
            {
                LineJoin = LineJoin.Round,
            };

            graphics.FillPath(bubbleBrush, bubble);
            graphics.DrawPath(bubbleOutline, bubble);

            PointF[] tail =
            {
                new PointF(210, 665),
                new PointF(150, 810),
                new PointF(385, 681),
            };
            graphics.FillPolygon(bubbleBrush, tail);
            graphics.DrawLines(bubbleOutline, tail);

            using var connectionPen = new Pen(Color.FromArgb(210, 222, 244, 255), 26)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            graphics.DrawBezier(
                connectionPen,
                new PointF(245, 410),
                new PointF(330, 265),
                new PointF(485, 260),
                new PointF(570, 408));

            using var nodeBrush = new SolidBrush(Color.FromArgb(255, 239, 250, 255));
            DrawCircle(graphics, nodeBrush, 245, 410, 38);
            DrawCircle(graphics, nodeBrush, 405, 310, 48);
            DrawCircle(graphics, nodeBrush, 570, 408, 38);

            using var lensBrush = new SolidBrush(Color.FromArgb(105, 10, 32, 54));
            graphics.FillEllipse(lensBrush, 393, 370, 430, 430);

            using var magnifierShadow = new Pen(Color.FromArgb(255, 17, 32, 52), 116)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            using var magnifier = new Pen(Color.FromArgb(255, 91, 214, 255), 68)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };

            graphics.DrawEllipse(magnifierShadow, 393, 370, 430, 430);
            graphics.DrawLine(magnifierShadow, 752, 728, 916, 892);
            graphics.DrawEllipse(magnifier, 393, 370, 430, 430);
            graphics.DrawLine(magnifier, 752, 728, 916, 892);

            using var sparkBrush = new SolidBrush(Color.White);
            using GraphicsPath largeSpark = CreateSpark(
                new PointF(608, 578),
                118,
                42);
            using GraphicsPath smallSpark = CreateSpark(
                new PointF(690, 485),
                48,
                18);
            graphics.FillPath(sparkBrush, largeSpark);
            graphics.FillPath(sparkBrush, smallSpark);
        }

        string pngPath = Path.Combine(outputDirectory, "CopilotSessionSearch.png");
        using (var preview = Resize(source, 256))
        {
            preview.Save(pngPath, ImageFormat.Png);
        }

        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
        var frames = new byte[sizes.Length][];
        for (int index = 0; index < sizes.Length; index++)
        {
            int size = sizes[index];
            using Bitmap frame = Resize(source, size);
            using var stream = new MemoryStream();
            frame.Save(stream, ImageFormat.Png);
            frames[index] = stream.ToArray();
        }

        WriteIco(
            Path.Combine(outputDirectory, "CopilotSessionSearch.ico"),
            sizes,
            frames);
    }

    private static Bitmap Resize(Bitmap source, int size)
    {
        var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(result);
        graphics.Clear(Color.Transparent);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, size, size),
            0,
            0,
            source.Width,
            source.Height,
            GraphicsUnit.Pixel);
        return result;
    }

    private static GraphicsPath CreateRoundedRectangle(RectangleF rectangle, float radius)
    {
        float diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(
            rectangle.Right - diameter,
            rectangle.Bottom - diameter,
            diameter,
            diameter,
            0,
            90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath CreateSpark(PointF center, float outerRadius, float innerRadius)
    {
        var path = new GraphicsPath();
        PointF[] points =
        {
            new PointF(center.X, center.Y - outerRadius),
            new PointF(center.X + innerRadius, center.Y - innerRadius),
            new PointF(center.X + outerRadius, center.Y),
            new PointF(center.X + innerRadius, center.Y + innerRadius),
            new PointF(center.X, center.Y + outerRadius),
            new PointF(center.X - innerRadius, center.Y + innerRadius),
            new PointF(center.X - outerRadius, center.Y),
            new PointF(center.X - innerRadius, center.Y - innerRadius),
        };
        path.AddPolygon(points);
        return path;
    }

    private static void DrawCircle(
        Graphics graphics,
        Brush brush,
        float centerX,
        float centerY,
        float radius)
    {
        graphics.FillEllipse(
            brush,
            centerX - radius,
            centerY - radius,
            radius * 2,
            radius * 2);
    }

    private static void WriteIco(string path, int[] sizes, byte[][] frames)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Length);

        int offset = 6 + (16 * frames.Length);
        for (int index = 0; index < frames.Length; index++)
        {
            int size = sizes[index];
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)frames[index].Length);
            writer.Write((uint)offset);
            offset += frames[index].Length;
        }

        foreach (byte[] frame in frames)
        {
            writer.Write(frame);
        }
    }
}
'@

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$drawingAssemblies = @(
    [AppContext]::GetData('TRUSTED_PLATFORM_ASSEMBLIES').Split(
        [System.IO.Path]::PathSeparator,
        [System.StringSplitOptions]::RemoveEmptyEntries)
    [object].Assembly.Location
    [System.Drawing.Bitmap].Assembly.Location
    [System.Drawing.PointF].Assembly.Location
    (Join-Path $PSHOME 'System.Private.Windows.GdiPlus.dll')
    (Join-Path $PSHOME 'System.Private.Windows.Core.dll')
) | Select-Object -Unique
Add-Type -TypeDefinition $source -ReferencedAssemblies $drawingAssemblies
[AppIconGenerator]::Generate([System.IO.Path]::GetFullPath($OutputDirectory))

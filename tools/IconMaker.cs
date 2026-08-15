using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class IconMaker
{
    private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: IconMaker <source.png> <output.ico>");
            return 2;
        }

        try
        {
            using (var source = new Bitmap(args[0]))
            {
                ValidateTransparency(source);
                WriteIcon(source, args[1]);
            }

            Console.WriteLine("Icon generated: " + args[1]);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }
    }

    private static void ValidateTransparency(Bitmap source)
    {
        var corners = new[]
        {
            source.GetPixel(0, 0),
            source.GetPixel(source.Width - 1, 0),
            source.GetPixel(0, source.Height - 1),
            source.GetPixel(source.Width - 1, source.Height - 1)
        };

        foreach (var corner in corners)
        {
            if (corner.A > 16)
            {
                throw new InvalidOperationException("The source icon does not have transparent corners.");
            }
        }
    }

    private static void WriteIcon(Bitmap source, string outputPath)
    {
        var images = new List<byte[]>();
        foreach (var size in Sizes)
        {
            using (var resized = Resize(source, size))
            using (var stream = new MemoryStream())
            {
                resized.Save(stream, ImageFormat.Png);
                images.Add(stream.ToArray());
            }
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        Directory.CreateDirectory(directory);

        using (var file = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(file))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)images.Count);

            var offset = 6 + images.Count * 16;
            for (var index = 0; index < images.Count; index++)
            {
                var size = Sizes[index];
                writer.Write((byte)(size == 256 ? 0 : size));
                writer.Write((byte)(size == 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write((uint)images[index].Length);
                writer.Write((uint)offset);
                offset += images[index].Length;
            }

            foreach (var image in images)
            {
                writer.Write(image);
            }
        }
    }

    private static Bitmap Resize(Bitmap source, int size)
    {
        var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        result.SetResolution(96, 96);

        using (var graphics = Graphics.FromImage(result))
        using (var attributes = new ImageAttributes())
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, size, size),
                0,
                0,
                source.Width,
                source.Height,
                GraphicsUnit.Pixel,
                attributes);
        }

        return result;
    }
}

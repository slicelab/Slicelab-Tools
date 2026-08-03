using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;

namespace Slicelab.PDF
{
    /// <summary>
    /// Decodes images for PdfSharpCore using System.Drawing instead of the bundled ImageSharp path.
    ///
    /// PdfSharpCore decodes rasters through ImageSharp, calling an overload that was removed in
    /// ImageSharp 3.0. Grasshopper loads every .gha's dependencies into one AssemblyLoadContext keyed
    /// by simple name, so only one SixLabors.ImageSharp can be resolved per session — if another
    /// installed plugin ships 3.x and loads first, PdfSharpCore binds to it and every image throws
    /// MissingMethodException.
    ///
    /// Replacing ImageSourceImpl takes ImageSharp off the code path entirely: .NET resolves methods
    /// lazily at JIT time, so a method that is never called never resolves its broken reference.
    /// </summary>
    public class PdfImageSource : ImageSource
    {
        private static bool _registered;

        /// <summary>
        /// Claim PdfSharpCore's image decoder. Must run before any PDF component executes —
        /// XImage.FromFile/FromStream install the ImageSharp default only if ImageSourceImpl is null,
        /// so whoever sets it first wins. Called from SlicelabPriority.PriorityLoad().
        /// </summary>
        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            ImageSourceImpl = new PdfImageSource();
        }

        protected override IImageSource FromFileImpl(string path, int? quality = 75)
        {
            byte[] data;
            try { data = File.ReadAllBytes(path); }
            catch (Exception ex) { throw Stage("read", ex); }
            return Build(path, data, quality);
        }

        protected override IImageSource FromBinaryImpl(string name, Func<byte[]> imageSource, int? quality = 75)
        {
            return Build(name, imageSource.Invoke(), quality);
        }

        protected override IImageSource FromStreamImpl(string name, Func<Stream> imageStream, int? quality = 75)
        {
            byte[] data;
            using (var stream = imageStream.Invoke())
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                data = ms.ToArray();
            }
            return Build(name, data, quality);
        }

        private static IImageSource Build(string name, byte[] data, int? quality)
        {
            Bitmap bitmap;
            try
            {
                // Copy into a private bitmap: System.Drawing keeps the source stream alive for the
                // lifetime of a stream-constructed Bitmap, and we want the buffer released here.
                using (var ms = new MemoryStream(data))
                using (var loaded = new Bitmap(ms))
                    bitmap = new Bitmap(loaded);
            }
            catch (Exception ex)
            {
                throw Stage("decode", ex);
            }

            return new SystemDrawingImageSource(name, bitmap, quality ?? 75, IsPng(data));
        }

        /// <summary>
        /// PdfSharpCore's own implementation treats "is a PNG" as "may be transparent", routing PNGs
        /// through the lossless bitmap path and everything else through JPEG. Mirror that, but detect
        /// it from the file signature rather than the extension.
        /// </summary>
        private static bool IsPng(byte[] data)
        {
            return data != null && data.Length >= 8 &&
                   data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
                   data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;
        }

        private static Exception Stage(string stage, Exception inner)
        {
            return new InvalidOperationException($"PDF image — {stage}: {inner.Message}", inner);
        }

        private class SystemDrawingImageSource : IImageSource
        {
            private readonly Bitmap _bitmap;
            private readonly int _quality;

            public int Width => _bitmap.Width;
            public int Height => _bitmap.Height;
            public string Name { get; }
            public bool Transparent { get; }

            public SystemDrawingImageSource(string name, Bitmap bitmap, int quality, bool transparent)
            {
                Name = name;
                _bitmap = bitmap;
                _quality = quality;
                Transparent = transparent;
            }

            public void SaveAsJpeg(MemoryStream ms)
            {
                try
                {
                    var codec = GetEncoder(ImageFormat.Jpeg);
                    if (codec == null)
                    {
                        _bitmap.Save(ms, ImageFormat.Jpeg);
                        return;
                    }

                    using (var parameters = new EncoderParameters(1))
                    using (var quality = new EncoderParameter(Encoder.Quality, (long)_quality))
                    {
                        parameters.Param[0] = quality;
                        _bitmap.Save(ms, codec, parameters);
                    }
                }
                catch (Exception ex)
                {
                    throw Stage("jpeg encode", ex);
                }
            }

            /// <summary>
            /// Write a 32bpp BGRA Windows bitmap by hand rather than through the GDI+ BMP encoder.
            ///
            /// PdfSharpCore's PdfImage.ReadTrueColorMemoryBitmap parses these bytes directly and throws
            /// on anything unexpected, but it detects transparency by scanning alpha values — so an
            /// encoder that silently drops the alpha channel produces an opaque image with no error at
            /// all. Building the header ourselves makes that failure impossible and behaves identically
            /// on macOS (libgdiplus) and Windows (GDI+).
            /// </summary>
            public void SaveAsPdfBitmap(MemoryStream ms)
            {
                try
                {
                    int width = _bitmap.Width;
                    int height = _bitmap.Height;
                    const int headerSize = 54;              // BITMAPFILEHEADER (14) + BITMAPINFOHEADER (40)
                    int rowBytes = width * 4;               // 32bpp rows are inherently 4-byte aligned
                    int pixelBytes = rowBytes * height;
                    int fileSize = headerSize + pixelBytes;

                    var buffer = new byte[fileSize];

                    // BITMAPFILEHEADER
                    buffer[0] = 0x42;                       // 'B'
                    buffer[1] = 0x4D;                       // 'M'
                    WriteInt32(buffer, 2, fileSize);        // total file length
                    WriteInt32(buffer, 10, headerSize);     // offset to pixel data

                    // BITMAPINFOHEADER
                    WriteInt32(buffer, 14, 40);             // header size — PdfSharpCore requires exactly 40
                    WriteInt32(buffer, 18, width);
                    WriteInt32(buffer, 22, height);
                    WriteInt16(buffer, 26, 1);              // planes
                    WriteInt16(buffer, 28, 32);             // bits per pixel
                    WriteInt32(buffer, 30, 0);              // BI_RGB, no compression
                    WriteInt32(buffer, 34, pixelBytes);
                    // Remaining fields (resolution, palette counts) stay zero.

                    CopyPixelsBottomUp(buffer, headerSize, rowBytes, width, height);
                    AssertHeader(buffer, fileSize, width, height);

                    ms.Write(buffer, 0, buffer.Length);
                }
                catch (Exception ex)
                {
                    throw Stage("bmp encode", ex);
                }
            }

            /// <summary>Copy BGRA rows in bottom-up order, the row order a Windows bitmap expects.</summary>
            private void CopyPixelsBottomUp(byte[] buffer, int offset, int rowBytes, int width, int height)
            {
                var rect = new Rectangle(0, 0, width, height);
                BitmapData data = _bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < height; y++)
                    {
                        IntPtr rowStart = IntPtr.Add(data.Scan0, y * data.Stride);
                        int destRow = height - 1 - y;
                        Marshal.Copy(rowStart, buffer, offset + destRow * rowBytes, rowBytes);
                    }
                }
                finally
                {
                    _bitmap.UnlockBits(data);
                }
            }

            /// <summary>
            /// Re-read the header we just wrote against the exact checks PdfSharpCore performs, so a
            /// mistake here fails with the offending field named instead of a generic
            /// "unsupported format" from inside PdfSharpCore.
            /// </summary>
            private static void AssertHeader(byte[] buffer, int fileSize, int width, int height)
            {
                Check(buffer[0] == 0x42 && buffer[1] == 0x4D, "signature", "BM");
                Check(ReadInt32(buffer, 2) == fileSize, "file size", fileSize.ToString());
                Check(ReadInt32(buffer, 14) == 40, "info header size", "40");
                Check(ReadInt32(buffer, 18) == width, "width", width.ToString());
                Check(ReadInt32(buffer, 22) == height, "height", height.ToString());
                Check(ReadInt16(buffer, 26) == 1, "planes", "1");
                Check(ReadInt16(buffer, 28) == 32, "bits per pixel", "32");
                Check(ReadInt32(buffer, 30) == 0, "compression", "0 (BI_RGB)");
            }

            private static void Check(bool ok, string field, string expected)
            {
                if (!ok)
                    throw new InvalidOperationException($"bitmap header field '{field}' is not {expected}");
            }

            private static ImageCodecInfo GetEncoder(ImageFormat format)
            {
                foreach (var codec in ImageCodecInfo.GetImageEncoders())
                    if (codec.FormatID == format.Guid) return codec;
                return null;
            }

            private static void WriteInt32(byte[] b, int offset, int value)
            {
                b[offset] = (byte)value;
                b[offset + 1] = (byte)(value >> 8);
                b[offset + 2] = (byte)(value >> 16);
                b[offset + 3] = (byte)(value >> 24);
            }

            private static void WriteInt16(byte[] b, int offset, int value)
            {
                b[offset] = (byte)value;
                b[offset + 1] = (byte)(value >> 8);
            }

            private static int ReadInt32(byte[] b, int offset)
            {
                return b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24);
            }

            private static int ReadInt16(byte[] b, int offset)
            {
                return b[offset] | (b[offset + 1] << 8);
            }
        }

        /// <summary>
        /// Turn an image failure into something a user can act on. The plugin-conflict case should now
        /// be unreachable, but stays as a safety net in case a future PdfSharpCore path reaches
        /// ImageSharp some other way.
        /// </summary>
        public static string Describe(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is MissingMethodException || e is TypeLoadException || e is MissingMemberException)
                    return "PDF imaging unavailable — another installed plugin loaded an incompatible imaging library";
            }
            return ex.Message;
        }
    }
}

<#
.SYNOPSIS
  Gera o AudioRouter.ico (multi-tamanho) a partir da imagem oficial do ícone.

.DESCRIPTION
  A imagem de origem tem um halo quase invisível (alfa 1-10) fora do tile arredondado, que vira ruído em
  tamanhos pequenos. O script recorta o tile, zera esse halo (preservando o antialias da borda) e gera
  as resoluções 16, 20, 24, 32, 40, 48, 64, 128 e 256 px (16-32 cobrem 100%-200% de DPI da bandeja).
  Tamanhos até 48 px usam entradas BMP (máxima compatibilidade); 64 px ou mais usam PNG.

.EXAMPLE
  .\tools\make-icon.ps1
  .\tools\make-icon.ps1 -Source .\src\AudioRouter.App\Assets\icon-source.png -Out .\src\AudioRouter.App\Assets\AudioRouter.ico
#>
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\src\AudioRouter.App\Assets\icon-source.png'),
    [string]$Out = (Join-Path $PSScriptRoot '..\src\AudioRouter.App\Assets\AudioRouter.ico'),
    [string]$PreviewDir = ''
)

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class IconBuilder
{
    // Alfa abaixo de LowAlpha vira 0 (halo); acima de HighAlpha vira 255 (interior do tile). Entre os dois, rampa linear.
    const int LowAlpha = 30, HighAlpha = 200;

    public static Bitmap CropAndClean(string path, double padding)
    {
        using (var src = new Bitmap(path))
        {
            int w = src.Width, h = src.Height;
            var data = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var buf = new byte[data.Stride * h];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            src.UnlockBits(data);

            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (buf[y * data.Stride + x * 4 + 3] >= HighAlpha)
                    {
                        if (x < minX) minX = x; if (x > maxX) maxX = x;
                        if (y < minY) minY = y; if (y > maxY) maxY = y;
                    }
            if (maxX < 0) throw new InvalidOperationException("A imagem não tem pixels opacos.");

            int side = (int)Math.Ceiling(Math.Max(maxX - minX + 1, maxY - minY + 1) * (1.0 + padding));
            int ox = (minX + maxX + 1) / 2 - side / 2, oy = (minY + maxY + 1) / 2 - side / 2;

            var dst = new Bitmap(side, side, PixelFormat.Format32bppArgb);
            var dd = dst.LockBits(new Rectangle(0, 0, side, side), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var outBuf = new byte[dd.Stride * side];
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                {
                    int sx = x + ox, sy = y + oy, o = y * dd.Stride + x * 4;
                    if (sx < 0 || sy < 0 || sx >= w || sy >= h) continue; // fora da imagem: transparente
                    int i = sy * data.Stride + sx * 4;
                    int a = buf[i + 3];
                    int na = a <= LowAlpha ? 0 : a >= HighAlpha ? 255 : (a - LowAlpha) * 255 / (HighAlpha - LowAlpha);
                    if (na == 0) continue;
                    outBuf[o] = buf[i]; outBuf[o + 1] = buf[i + 1]; outBuf[o + 2] = buf[i + 2]; outBuf[o + 3] = (byte)na;
                }
            System.Runtime.InteropServices.Marshal.Copy(outBuf, 0, dd.Scan0, outBuf.Length);
            dst.UnlockBits(dd);
            return dst;
        }
    }

    static Bitmap Scale(Bitmap src, int size, InterpolationMode mode)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = mode;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            using (var attr = new ImageAttributes())
            {
                attr.SetWrapMode(WrapMode.TileFlipXY); // evita borda semitransparente no recorte
                g.DrawImage(src, new Rectangle(0, 0, size, size), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attr);
            }
        }
        return bmp;
    }

    // Reduz pela metade enquanto der e termina com bicúbica: o GDI+ sozinho serrilha em reduções grandes.
    public static Bitmap Resize(Bitmap src, int size)
    {
        Bitmap cur = src; bool owned = false;
        while (cur.Width / 2 >= size)
        {
            var next = Scale(cur, cur.Width / 2, InterpolationMode.HighQualityBilinear);
            if (owned) cur.Dispose();
            cur = next; owned = true;
        }
        var result = Scale(cur, size, InterpolationMode.HighQualityBicubic);
        if (owned) cur.Dispose();
        return result;
    }

    static byte[] EncodePng(Bitmap bmp)
    {
        using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    static byte[] EncodeDib(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new byte[d.Stride * h];
        System.Runtime.InteropServices.Marshal.Copy(d.Scan0, px, 0, px.Length);
        bmp.UnlockBits(d);

        int maskStride = ((w + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write((short)1); bw.Write((short)32);
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            for (int y = h - 1; y >= 0; y--) bw.Write(px, y * d.Stride, w * 4); // linhas de baixo para cima
            bw.Write(new byte[maskStride * h]); // máscara AND toda zero: o alfa do canal faz o trabalho
            return ms.ToArray();
        }
    }

    public static void Build(Bitmap master, int[] sizes, string outPath, string previewDir)
    {
        var images = new List<byte[]>();
        foreach (int size in sizes)
        {
            using (var bmp = Resize(master, size))
            {
                images.Add(size >= 64 ? EncodePng(bmp) : EncodeDib(bmp));
                if (!string.IsNullOrEmpty(previewDir))
                    bmp.Save(Path.Combine(previewDir, "icon-" + size + ".png"), ImageFormat.Png);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
        using (var fs = File.Create(outPath))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                bw.Write((byte)(s >= 256 ? 0 : s)); bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)0); bw.Write((byte)0);
                bw.Write((short)1); bw.Write((short)32);
                bw.Write(images[i].Length); bw.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) bw.Write(img);
        }
    }
}
'@

$Source = (Resolve-Path $Source).Path
$Out = [System.IO.Path]::GetFullPath($Out)
if ($PreviewDir) { New-Item -ItemType Directory -Force $PreviewDir | Out-Null }

$master = [IconBuilder]::CropAndClean($Source, 0.03)
try {
    [IconBuilder]::Build($master, [int[]](16, 20, 24, 32, 40, 48, 64, 128, 256), $Out, $PreviewDir)
}
finally { $master.Dispose() }

"Gerado: $Out ($([math]::Round((Get-Item $Out).Length / 1KB)) KB)"

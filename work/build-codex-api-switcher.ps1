$ErrorActionPreference = "Stop"

$source = Join-Path $PSScriptRoot "CodexApiSwitcher.cs"
$workspace = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $workspace "outputs"
$output = Join-Path $outputDirectory "CodexApiSwitcher.exe"
$icon = Join-Path $outputDirectory "cas-logo.ico"

if (!(Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output
}

if (Test-Path -LiteralPath $icon) {
    Remove-Item -LiteralPath $icon
}

Add-Type `
    -ReferencedAssemblies @("System.Drawing.dll") `
    -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;

public static class CasIconBuild
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static void Save(string path)
    {
        using (Bitmap bitmap = CreateBitmap(256))
        using (MemoryStream pngStream = new MemoryStream())
        {
            bitmap.Save(pngStream, System.Drawing.Imaging.ImageFormat.Png);
            byte[] png = pngStream.ToArray();

            using (FileStream stream = File.Create(path))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)1);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(png.Length);
                writer.Write(22);
                writer.Write(png);
            }
        }
    }

    private static Bitmap CreateBitmap(int size)
    {
        Bitmap bitmap = new Bitmap(size, size);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            float scale = size / 64F;
            RectangleF outer = new RectangleF(5 * scale, 5 * scale, 54 * scale, 54 * scale);
            using (GraphicsPath outerPath = RoundedRectangle(outer, 14 * scale))
            using (SolidBrush background = new SolidBrush(Color.White))
            {
                graphics.FillPath(background, outerPath);
            }

            RectangleF inset = new RectangleF(10 * scale, 10 * scale, 44 * scale, 44 * scale);
            using (GraphicsPath insetPath = RoundedRectangle(inset, 10 * scale))
            {
                graphics.SetClip(insetPath);
                using (SolidBrush left = new SolidBrush(Color.FromArgb(210, 226, 255)))
                using (SolidBrush right = new SolidBrush(Color.FromArgb(185, 238, 238)))
                {
                    graphics.FillRectangle(left, inset.Left, inset.Top, inset.Width / 2F, inset.Height);
                    graphics.FillRectangle(right, inset.Left + inset.Width / 2F, inset.Top, inset.Width / 2F, inset.Height);
                }
                graphics.ResetClip();
            }

            using (Pen divider = new Pen(Color.FromArgb(202, 213, 226), Math.Max(1F, 1.5F * scale)))
            {
                graphics.DrawLine(divider, size / 2F, 13 * scale, size / 2F, size - 13 * scale);
            }

            using (GraphicsPath outerPath = RoundedRectangle(outer, 14 * scale))
            using (Pen border = new Pen(Color.FromArgb(23, 32, 51), Math.Max(2F, 3F * scale)))
            {
                graphics.DrawPath(border, outerPath);
            }

            using (Font font = new Font("Segoe UI", 17F * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush text = new SolidBrush(Color.FromArgb(23, 32, 51)))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.DrawString("CAS", font, text, new RectangleF(0, 1 * scale, size, size), format);
            }
        }

        return bitmap;
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        float diameter = radius * 2F;
        GraphicsPath path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
"@

[CasIconBuild]::Save($icon)

$compilerParameters = New-Object System.CodeDom.Compiler.CompilerParameters
$compilerParameters.CompilerOptions = '/win32icon:"' + $icon + '"'
$compilerParameters.GenerateExecutable = $true
$compilerParameters.GenerateInMemory = $false
$compilerParameters.OutputAssembly = $output
foreach ($reference in @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    "System.Security.dll",
    "System.Web.Extensions.dll",
    "System.Windows.Forms.dll"
)) {
    [void]$compilerParameters.ReferencedAssemblies.Add($reference)
}

Add-Type `
    -Path $source `
    -CompilerParameters $compilerParameters

Get-Item -LiteralPath $output, $icon | Select-Object FullName, Length, LastWriteTime

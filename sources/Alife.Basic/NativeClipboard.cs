using System.Collections.Specialized;
using System.IO;
using System.Windows.Media.Imaging;
using Alife.Basic;
using WpfClipboard=System.Windows.Clipboard;

namespace Alife.Components;

public static class NativeClipboard
{
    public static string? GetPastedContent()
    {
        try
        {
            if (WpfClipboard.ContainsFileDropList())
            {
                StringCollection files = WpfClipboard.GetFileDropList();
                if (files.Count > 0)
                    return string.Join("\n", files.Cast<string>());
            }

            if (WpfClipboard.ContainsImage())
            {
                BitmapSource? image = WpfClipboard.GetImage();
                if (image != null)
                {
                    string dir = Path.Combine(AlifePath.TempFolderPath, "PastedImages");
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, $"paste_{DateTime.Now:yyyyMMdd_HHmmssfff}.png");

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    using var fs = File.Create(path);
                    encoder.Save(fs);

                    return path;
                }
            }

            if (WpfClipboard.ContainsText())
                return WpfClipboard.GetText();

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Clipboard read failed: {ex.Message}");
            return null;
        }
    }
}

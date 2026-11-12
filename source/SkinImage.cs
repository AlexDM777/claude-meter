using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ClaudeMeter {
public static class SkinImage {
    [DllImport("libwebp.dll",CallingConvention=CallingConvention.Cdecl)] static extern int WebPGetInfo(byte[] data,UIntPtr length,out int width,out int height);
    [DllImport("libwebp.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr WebPDecodeBGRAInto(byte[] data,UIntPtr length,byte[] pixels,UIntPtr size,int stride);
    [DllImport("libwebp.dll",CallingConvention=CallingConvention.Cdecl)] static extern UIntPtr WebPEncodeBGRA(byte[] pixels,int width,int height,int stride,float quality,out IntPtr output);
    [DllImport("libwebp.dll",CallingConvention=CallingConvention.Cdecl)] static extern void WebPFree(IntPtr output);
    public static BitmapSource Load(string path){
        if(!path.EndsWith(".webp",StringComparison.OrdinalIgnoreCase)){using(var stream=File.OpenRead(path)){var image=new PngBitmapDecoder(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];image.Freeze();return image;}}
        var data=File.ReadAllBytes(path);int w,h;if(data.Length<12||System.Text.Encoding.ASCII.GetString(data,0,4)!="RIFF"||System.Text.Encoding.ASCII.GetString(data,8,4)!="WEBP"||WebPGetInfo(data,(UIntPtr)data.Length,out w,out h)==0||w<=0||h<=0||w>8192||h>8192)throw new Exception("Invalid WebP image");
        int stride=checked(w*4);var pixels=new byte[checked(stride*h)];if(WebPDecodeBGRAInto(data,(UIntPtr)data.Length,pixels,(UIntPtr)pixels.Length,stride)==IntPtr.Zero)throw new Exception("WebP decode failed");
        var result=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,pixels,stride);result.Freeze();return result;
    }
    public static System.Drawing.Bitmap Bitmap(string path){var image=Load(path);var converted=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);var result=new System.Drawing.Bitmap(image.PixelWidth,image.PixelHeight,System.Drawing.Imaging.PixelFormat.Format32bppArgb);var bits=result.LockBits(new System.Drawing.Rectangle(0,0,result.Width,result.Height),System.Drawing.Imaging.ImageLockMode.WriteOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);try{converted.CopyPixels(System.Windows.Int32Rect.Empty,bits.Scan0,bits.Stride*result.Height,bits.Stride);}finally{result.UnlockBits(bits);}return result;}
    public static void SaveWebP(BitmapSource image,string path){var converted=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);int stride=image.PixelWidth*4;var pixels=new byte[stride*image.PixelHeight];converted.CopyPixels(pixels,stride,0);IntPtr output;ulong size=WebPEncodeBGRA(pixels,image.PixelWidth,image.PixelHeight,stride,92,out output).ToUInt64();if(size==0||output==IntPtr.Zero)throw new Exception("WebP encode failed");try{var bytes=new byte[checked((int)size)];Marshal.Copy(output,bytes,0,bytes.Length);File.WriteAllBytes(path,bytes);}finally{WebPFree(output);}}
}
}

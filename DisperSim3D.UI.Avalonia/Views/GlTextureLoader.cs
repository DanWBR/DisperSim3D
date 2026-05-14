#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.OpenGL;
using SkiaSharp;
using static Avalonia.OpenGL.GlConsts;

namespace DisperSim3D.UI.Avalonia.Views
{
    internal static class GlTextureLoader
    {
        private const int GL_TEXTURE_2D = 0x0DE1;
        private const int GL_TEXTURE_MIN_FILTER = 0x2801;
        private const int GL_TEXTURE_MAG_FILTER = 0x2800;
        private const int GL_TEXTURE_WRAP_S = 0x2802;
        private const int GL_TEXTURE_WRAP_T = 0x2803;
        private const int GL_LINEAR_MIPMAP_LINEAR = 0x2703;
        private const int GL_LINEAR = 0x2601;
        private const int GL_REPEAT = 0x2901;
        private const int GL_RGBA_VAL = 0x1908;
        private const int GL_UNSIGNED_BYTE_VAL = 0x1401;

        private delegate void D_glGenerateMipmap(int target);
        private delegate void D_glBindTexture(int target, int texture);
        private delegate void D_glTexParameteri(int target, int pname, int param);
        private unsafe delegate void D_glTexImage2D(
            int target, int level, int internalFormat,
            int width, int height, int border,
            int format, int type, IntPtr data);

        private static D_glGenerateMipmap? _genMip;
        private static D_glBindTexture? _bindTex;
        private static D_glTexParameteri? _texParam;
        private static D_glTexImage2D? _texImage2D;
        private static bool _loaded;

        private static void EnsureLoaded(GlInterface gl)
        {
            if (_loaded) return;
            _loaded = true;
            T? Get<T>(string name) where T : Delegate
            {
                var ptr = gl.GetProcAddress(name);
                return ptr == IntPtr.Zero ? null
                    : System.Runtime.InteropServices.Marshal
                        .GetDelegateForFunctionPointer<T>(ptr);
            }
            _genMip = Get<D_glGenerateMipmap>("glGenerateMipmap");
            _bindTex = Get<D_glBindTexture>("glBindTexture");
            _texParam = Get<D_glTexParameteri>("glTexParameteri");
            _texImage2D = Get<D_glTexImage2D>("glTexImage2D");
        }

        public static unsafe int LoadFromFile(GlInterface gl, string path)
        {
            if (!File.Exists(path)) return 0;
            EnsureLoaded(gl);
            if (_bindTex == null || _texImage2D == null) return 0;

            try
            {
                using var bitmap = SKBitmap.Decode(path);
                if (bitmap == null) return 0;

                using var rgba = bitmap.ColorType == SKColorType.Rgba8888
                    ? bitmap
                    : bitmap.Copy(SKColorType.Rgba8888);
                if (rgba == null) return 0;

                int w = rgba.Width, h = rgba.Height;
                var pixels = rgba.GetPixelSpan();

                int tex = gl.GenTexture();
                _bindTex(GL_TEXTURE_2D, tex);

                fixed (byte* ptr = pixels)
                {
                    _texImage2D(GL_TEXTURE_2D, 0, GL_RGBA_VAL,
                        w, h, 0, GL_RGBA_VAL, GL_UNSIGNED_BYTE_VAL, (IntPtr)ptr);
                }

                _texParam?.Invoke(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_REPEAT);
                _texParam?.Invoke(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_REPEAT);
                _texParam?.Invoke(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);

                if (_genMip != null)
                {
                    _texParam?.Invoke(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER,
                        GL_LINEAR_MIPMAP_LINEAR);
                    _genMip(GL_TEXTURE_2D);
                }
                else
                {
                    _texParam?.Invoke(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
                }

                _bindTex(GL_TEXTURE_2D, 0);
                return tex;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GlTextureLoader] Failed: {path}: {ex.Message}");
                return 0;
            }
        }

        public static int CreateWhite1x1(GlInterface gl)
        {
            EnsureLoaded(gl);
            if (_bindTex == null || _texImage2D == null) return 0;

            int tex = gl.GenTexture();
            _bindTex(GL_TEXTURE_2D, tex);

            byte[] white = { 255, 255, 255, 255 };
            unsafe
            {
                fixed (byte* ptr = white)
                {
                    _texImage2D(GL_TEXTURE_2D, 0, GL_RGBA_VAL,
                        1, 1, 0, GL_RGBA_VAL, GL_UNSIGNED_BYTE_VAL, (IntPtr)ptr);
                }
            }

            _texParam?.Invoke(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
            _texParam?.Invoke(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            _bindTex(GL_TEXTURE_2D, 0);
            return tex;
        }
    }
}

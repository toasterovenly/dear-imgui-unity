using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using Unity.Collections.LowLevel.Unsafe;

namespace ImGuiNET
{
    public unsafe partial struct ImFontAtlasCustomRect
    {
        public ushort Width;
        public ushort Height;
        public ushort X;
        public ushort Y;
        public uint GlyphID;
        public float GlyphAdvanceX;
        public Vector2 GlyphOffset;
        public ImFont* Font;
    }
    public unsafe partial struct ImFontAtlasCustomRectPtr
    {
        public ImFontAtlasCustomRect* NativePtr { get; }
        public ImFontAtlasCustomRectPtr(ImFontAtlasCustomRect* nativePtr) => NativePtr = nativePtr;
        public ImFontAtlasCustomRectPtr(IntPtr nativePtr) => NativePtr = (ImFontAtlasCustomRect*)nativePtr;
        public static implicit operator ImFontAtlasCustomRectPtr(ImFontAtlasCustomRect* nativePtr) => new ImFontAtlasCustomRectPtr(nativePtr);
        public static implicit operator ImFontAtlasCustomRect* (ImFontAtlasCustomRectPtr wrappedPtr) => wrappedPtr.NativePtr;
        public static implicit operator ImFontAtlasCustomRectPtr(IntPtr nativePtr) => new ImFontAtlasCustomRectPtr(nativePtr);
        public ref ushort Width => ref UnsafeUtility.AsRef<ushort>(&NativePtr->Width);
        public ref ushort Height => ref UnsafeUtility.AsRef<ushort>(&NativePtr->Height);
        public ref ushort X => ref UnsafeUtility.AsRef<ushort>(&NativePtr->X);
        public ref ushort Y => ref UnsafeUtility.AsRef<ushort>(&NativePtr->Y);
        public ref uint GlyphID => ref UnsafeUtility.AsRef<uint>(&NativePtr->GlyphID);
        public ref float GlyphAdvanceX => ref UnsafeUtility.AsRef<float>(&NativePtr->GlyphAdvanceX);
        public ref Vector2 GlyphOffset => ref UnsafeUtility.AsRef<Vector2>(&NativePtr->GlyphOffset);
        public ImFontPtr Font => new ImFontPtr(NativePtr->Font);
        public void Destroy()
        {
            ImGuiNative.ImFontAtlasCustomRect_destroy((ImFontAtlasCustomRect*)(NativePtr));
        }
        public bool IsPacked()
        {
            byte ret = ImGuiNative.ImFontAtlasCustomRect_IsPacked((ImFontAtlasCustomRect*)(NativePtr));
            return ret != 0;
        }
    }
}

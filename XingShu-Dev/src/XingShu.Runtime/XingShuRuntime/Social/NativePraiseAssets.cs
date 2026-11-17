using System;
using FairyGUI;
using UnityEngine;

namespace XingShuRuntime.Social
{
    internal static partial class NativePraiseAssets
    {
        private static NTexture _atlas, _blue, _color;
        internal static NTexture Get(bool selected)
        {
            if (_atlas == null || _atlas.disposed)
            {
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.name = "XingShu_OriginalPraiseAtlas";
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                if (!ImageConversion.LoadImage(texture, Convert.FromBase64String(AtlasPng)))
                { UnityEngine.Object.Destroy(texture); throw new InvalidOperationException("原版点赞图集读取失败"); }
                _atlas = new NTexture(texture);
                // Original 1920x768 game atlas; top-left pixels, without recoloring.
                _blue = new NTexture(_atlas, new Rect(475, 530, 150, 150), false);
                _color = new NTexture(_atlas, new Rect(475, 371, 150, 150), false);
            }
            return selected ? _color : _blue;
        }
        internal static void Reset()
        {
            if (_blue != null && !_blue.disposed) _blue.Dispose();
            if (_color != null && !_color.disposed) _color.Dispose();
            if (_atlas != null && !_atlas.disposed) _atlas.Dispose();
            _blue = _color = _atlas = null;
        }
    }
}

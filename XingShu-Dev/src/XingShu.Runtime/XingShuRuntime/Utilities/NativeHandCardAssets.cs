using System;
using FairyGUI;
using UnityEngine;

namespace XingShuRuntime.Utilities
{
    internal static partial class NativeHandCardAssets
    {
        private static NTexture _cardBack;

        internal static NTexture Get()
        {
            if (_cardBack == null || _cardBack.disposed)
            {
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.name = "XingShu_OriginalHandCardBack";
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                if (!ImageConversion.LoadImage(texture, Convert.FromBase64String(CardBackPng)))
                {
                    UnityEngine.Object.Destroy(texture);
                    throw new InvalidOperationException("原版手牌卡背图标读取失败");
                }
                _cardBack = new NTexture(texture);
            }
            return _cardBack;
        }

        internal static void Reset()
        {
            if (_cardBack != null && !_cardBack.disposed) _cardBack.Dispose();
            _cardBack = null;
        }
    }
}

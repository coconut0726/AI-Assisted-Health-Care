using UnityEngine;

namespace AHC.VR
{
    /// <summary>Colours from the Figma "XR" page (same sage palette as the mobile app).</summary>
    public static class Theme
    {
        public static readonly Color Primary = Hex("5B8760");
        public static readonly Color PrimaryDark = Hex("3F6B45");
        public static readonly Color PrimaryBg = Hex("E2EEDD");
        public static readonly Color Text = Hex("1F2A22");
        public static readonly Color Muted = Hex("7B877D");
        public static readonly Color Border = Hex("E6ECE1");
        public static readonly Color Surface = Hex("F5F8F2");
        public static readonly Color Placeholder = Hex("C9D8C5");
        public static readonly Color Red = Hex("C2573F");
        public static readonly Color RedBg = Hex("FBE5DD");
        public static readonly Color Amber = Hex("A9781F");
        public static readonly Color AmberBg = Hex("FAF0CF");
        public static readonly Color Lav = Hex("6C6FA3");
        public static readonly Color LavBg = Hex("E9E9F6");
        public static readonly Color ChipDark = Hex("2F4A35");
        public static readonly Color HeroTop = Hex("7FA883");
        public static readonly Color HeroBottom = Hex("4F7C56");
        public static readonly Color White = Color.white;

        /// <summary>Panels float over passthrough, so they are nearly opaque.</summary>
        public static Color Panel => new Color(1f, 1f, 1f, 0.96f);

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}

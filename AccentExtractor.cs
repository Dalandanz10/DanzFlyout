using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DanzFlyout
{
    public static class AccentExtractor
    {
        public struct Palette
        {
            public Color Primary;
            public Color Secondary;
        }

        public static Color ExtractDominantColor(BitmapSource? source)
        {
            return ExtractPalette(source).Primary;
        }

        public static Palette ExtractPalette(BitmapSource? source)
        {
            if (source == null)
                return GetDefaultPalette();

            try
            {
                var thumb = new TransformedBitmap(source, new ScaleTransform(
                    48.0 / Math.Max(1, source.PixelWidth),
                    48.0 / Math.Max(1, source.PixelHeight)
                ));

                var formatted = new FormatConvertedBitmap(thumb, PixelFormats.Bgra32, null, 0);
                int width = formatted.PixelWidth;
                int height = formatted.PixelHeight;
                int stride = width * 4;
                byte[] pixels = new byte[height * stride];
                formatted.CopyPixels(pixels, stride, 0);

                var chromaticBuckets = new Dictionary<int, ColorCandidate>();
                long neutralPixels = 0;
                long brightNeutralPixels = 0;
                long darkNeutralPixels = 0;
                int totalValidPixels = 0;

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte b = pixels[i];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    byte a = pixels[i + 3];

                    if (a < 128) continue;
                    totalValidPixels++;

                    RgbToHsv(r, g, b, out float h, out float s, out float v);

                    // Keep neutral colors in the analysis instead of throwing
                    // whites/grays away. This prevents a tiny colored area from
                    // hijacking a black-and-white album cover.
                    if (s < 0.20f)
                    {
                        neutralPixels++;
                        if (v >= 0.70f) brightNeutralPixels++;
                        if (v < 0.25f) darkNeutralPixels++;
                        continue;
                    }

                    // Very dark chromatic pixels contain little useful accent
                    // information and tend to produce muddy visualizer colors.
                    if (v < 0.12f) continue;

                    int hueBucket = (int)(h / 15f) % 24;
                    if (!chromaticBuckets.TryGetValue(hueBucket, out var candidate))
                        candidate = new ColorCandidate { Hue = h };

                    candidate.TotalR += r;
                    candidate.TotalG += g;
                    candidate.TotalB += b;
                    candidate.Count++;
                    candidate.MaxSaturation = Math.Max(candidate.MaxSaturation, s);
                    candidate.MaxBrightness = Math.Max(candidate.MaxBrightness, v);
                    chromaticBuckets[hueBucket] = candidate;
                }

                if (totalValidPixels == 0)
                    return GetDefaultPalette();

                float neutralRatio = neutralPixels / (float)totalValidPixels;

                // For genuinely monochrome artwork, use a visible white/gray
                // palette instead of inventing a hue from tiny compression/color
                // artifacts. This handles both white-on-black and black-on-white.
                if (chromaticBuckets.Count == 0 || neutralRatio >= 0.70f)
                {
                    if (darkNeutralPixels > brightNeutralPixels)
                    {
                        return new Palette
                        {
                            Primary = Color.FromRgb(245, 245, 245),
                            Secondary = Color.FromRgb(170, 175, 185)
                        };
                    }

                    return new Palette
                    {
                        Primary = Color.FromRgb(255, 255, 255),
                        Secondary = Color.FromRgb(185, 190, 200)
                    };
                }

                // Normal chromatic extraction. Neutral colors are intentionally
                // not discarded from the source; chromatic accents are selected
                // only when the artwork actually contains enough color.
                var ranked = new List<ColorCandidate>(chromaticBuckets.Values);
                ranked.Sort((a, b) => b.GetScore().CompareTo(a.GetScore()));

                Color primary = ranked[0].ToColor();
                Color secondary = primary;
                bool foundDistinct = false;

                for (int i = 1; i < ranked.Count; i++)
                {
                    float hueDiff = Math.Abs(ranked[i].Hue - ranked[0].Hue);
                    if (hueDiff > 180f) hueDiff = 360f - hueDiff;

                    if (hueDiff >= 40f)
                    {
                        secondary = ranked[i].ToColor();
                        foundDistinct = true;
                        break;
                    }
                }

                // If the artwork contains a meaningful amount of neutral pixels,
                // prefer the actual neutral color instead of inventing a complementary
                // hue. This is important for covers such as ORANGE + WHITE:
                // the second color should be WHITE, not the complementary CYAN/TEAL.
                if (!foundDistinct)
                {
                    if (brightNeutralPixels > 0 && neutralRatio >= 0.03f)
                    {
                        secondary = Color.FromRgb(245, 245, 245);
                    }
                    else if (darkNeutralPixels > 0 && neutralRatio >= 0.03f)
                    {
                        secondary = Color.FromRgb(175, 180, 190);
                    }
                    else
                    {
                        secondary = CreateVisibleCompanion(primary);
                    }
                }

                return new Palette
                {
                    Primary = primary,
                    Secondary = secondary
                };
            }
            catch
            {
                return GetDefaultPalette();
            }
        }

        private static Palette GetDefaultPalette()
        {
            return new Palette
            {
                Primary = Color.FromRgb(255, 255, 255),
                Secondary = Color.FromRgb(180, 185, 195)
            };
        }

        private class ColorCandidate
        {
            public float Hue;
            public long TotalR;
            public long TotalG;
            public long TotalB;
            public int Count;
            public float MaxSaturation;
            public float MaxBrightness;

            public float GetScore() => Count * (MaxSaturation * 1.5f) * (MaxBrightness * 1.2f);

            public Color ToColor()
            {
                if (Count == 0) return Colors.White;

                byte r = (byte)(TotalR / Count);
                byte g = (byte)(TotalG / Count);
                byte b = (byte)(TotalB / Count);

                RgbToHsv(r, g, b, out float h, out float s, out float v);

                // Preserve the actual saturation. The previous extractor forced
                // low-saturation colors to 40%, which turned white/gray artwork
                // into artificial reds/oranges.
                if (v < 0.35f) v = 0.35f;

                return HsvToRgb(h, s, v);
            }
        }

        private static Color CreateVisibleCompanion(Color primary)
        {
            RgbToHsv(primary.R, primary.G, primary.B, out float h, out float s, out float v);

            // Keep the same hue family. Only use a companion hue when the album
            // genuinely has color; never default to an arbitrary orange/red.
            float companionHue = (h + 180f) % 360f;
            float companionSat = Math.Clamp(s * 0.75f, 0.20f, 0.85f);
            float companionVal = Math.Clamp(Math.Max(v, 0.75f), 0.75f, 1.0f);

            return HsvToRgb(companionHue, companionSat, companionVal);
        }

        public static void RgbToHsv(byte r, byte g, byte b, out float h, out float s, out float v)
        {
            float rf = r / 255f, gf = g / 255f, bf = b / 255f;
            float max = Math.Max(rf, Math.Max(gf, bf));
            float min = Math.Min(rf, Math.Min(gf, bf));
            float delta = max - min;

            v = max;
            s = max == 0 ? 0 : delta / max;

            if (delta == 0) h = 0;
            else if (max == rf) { h = 60 * (((gf - bf) / delta) % 6); if (h < 0) h += 360; }
            else if (max == gf) { h = 60 * (((bf - rf) / delta) + 2); }
            else { h = 60 * (((rf - gf) / delta) + 4); }
        }

        public static Color HsvToRgb(float h, float s, float v)
        {
            float c = v * s;
            float x = c * (1 - Math.Abs((h / 60f % 2) - 1));
            float m = v - c;

            float r = 0, g = 0, b = 0;
            if (h >= 0 && h < 60) { r = c; g = x; b = 0; }
            else if (h >= 60 && h < 120) { r = x; g = c; b = 0; }
            else if (h >= 120 && h < 180) { r = 0; g = c; b = x; }
            else if (h >= 180 && h < 240) { r = 0; g = x; b = c; }
            else if (h >= 240 && h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return Color.FromRgb(
                (byte)Math.Clamp((r + m) * 255, 0, 255),
                (byte)Math.Clamp((g + m) * 255, 0, 255),
                (byte)Math.Clamp((b + m) * 255, 0, 255)
            );
        }
    }
}
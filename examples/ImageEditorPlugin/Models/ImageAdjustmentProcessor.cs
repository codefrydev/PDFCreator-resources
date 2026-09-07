using SkiaSharp;

namespace PdfEditorApp.Plugins.ImageEditor.Models;

/// <summary>
/// Builds 4x5 RGBA color matrices for brightness/contrast/saturation/hue/temperature/tint
/// adjustments and a small set of named filter presets, plus spatial (blur/sharpen) filters,
/// applying them to raw image bytes via SkiaSharp. Mirrors the same color-matrix pattern
/// already used by the host app's own grayscale converter
/// (PdfOptimizationService.ConvertBitmapToGrayscale) for consistency across the product.
/// </summary>
public static class ImageAdjustmentProcessor
{
    // Standard luma coefficients — same ones used by the host app's grayscale converter.
    private const float LumaR = 0.21f, LumaG = 0.72f, LumaB = 0.07f;

    public static float[] Identity() => new float[]
    {
        1, 0, 0, 0, 0,
        0, 1, 0, 0, 0,
        0, 0, 1, 0, 0,
        0, 0, 0, 1, 0
    };

    /// <summary>brightness/contrast/saturation/temperature/tint are each -100..100, hue is
    /// -180..180; 0 = no change for all. Composed in order: saturation, contrast, brightness,
    /// hue, temperature, tint.</summary>
    public static float[] BuildMatrix(double brightness, double contrast, double saturation, double hue, double temperature, double tint)
    {
        var m = Identity();
        m = Compose(m, BuildSaturation(saturation));
        m = Compose(m, BuildContrast(contrast));
        m = Compose(m, BuildBrightness(brightness));
        m = Compose(m, BuildHueRotation(hue));
        m = Compose(m, BuildTemperature(temperature));
        m = Compose(m, BuildTint(tint));
        return m;
    }

    public static float[] BuildPresetMatrix(string preset) => preset switch
    {
        "BlackAndWhite" => BuildSaturation(-100),
        "Sepia" => new float[]
        {
            0.393f, 0.769f, 0.189f, 0, 0,
            0.349f, 0.686f, 0.168f, 0, 0,
            0.272f, 0.534f, 0.131f, 0, 0,
            0, 0, 0, 1, 0
        },
        "Vintage" => Compose(Compose(BuildSaturation(-40), BuildContrast(-12)), BuildBrightness(8)),
        "Vivid" => Compose(BuildSaturation(40), BuildContrast(15)),
        "Noir" => Compose(BuildSaturation(-100), BuildContrast(35)),
        "Invert" => new float[]
        {
            -1, 0, 0, 0, 255,
            0, -1, 0, 0, 255,
            0, 0, -1, 0, 255,
            0, 0, 0, 1, 0
        },
        "Duotone" => Compose(BuildSaturation(-100), BuildDuotoneMap(
            new SKColor(20, 20, 60), new SKColor(255, 170, 60))),
        // Vignette is a separate radial-overlay draw pass (see ImageElement.Draw), not a
        // color matrix — pass through unchanged so it composes cleanly with BCS/hue/temp/tint.
        "Vignette" => Identity(),
        _ => Identity()
    };

    /// <summary>Remaps post-desaturation luma (R=G=B already) to a fixed shadow→highlight
    /// gradient per channel. Divides by 3 since R+G+B all equal luma after desaturation.</summary>
    private static float[] BuildDuotoneMap(SKColor shadow, SKColor highlight)
    {
        float kR = (highlight.Red - shadow.Red) / (255f * 3f);
        float kG = (highlight.Green - shadow.Green) / (255f * 3f);
        float kB = (highlight.Blue - shadow.Blue) / (255f * 3f);
        return new float[]
        {
            kR, kR, kR, 0, shadow.Red,
            kG, kG, kG, 0, shadow.Green,
            kB, kB, kB, 0, shadow.Blue,
            0, 0, 0, 1, 0
        };
    }

    /// <summary>Composes two 4x5 matrices as "apply a, then apply b" (b ∘ a).</summary>
    public static float[] Compose(float[] a, float[] b)
    {
        var result = new float[20];
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 5; col++)
            {
                float sum = 0;
                for (int k = 0; k < 4; k++)
                {
                    sum += b[row * 5 + k] * a[k * 5 + col];
                }
                if (col == 4) sum += b[row * 5 + 4];
                result[row * 5 + col] = sum;
            }
        }
        return result;
    }

    private static float[] BuildBrightness(double brightness)
    {
        float offset = (float)(brightness / 100.0 * 255);
        return new float[]
        {
            1, 0, 0, 0, offset,
            0, 1, 0, 0, offset,
            0, 0, 1, 0, offset,
            0, 0, 0, 1, 0
        };
    }

    private static float[] BuildContrast(double contrast)
    {
        float factor = (float)((100 + contrast) / 100.0);
        float translate = 128 * (1 - factor);
        return new float[]
        {
            factor, 0, 0, 0, translate,
            0, factor, 0, 0, translate,
            0, 0, factor, 0, translate,
            0, 0, 0, 1, 0
        };
    }

    private static float[] BuildSaturation(double saturation)
    {
        float s = (float)(1 + saturation / 100.0);
        float invS = 1 - s;
        return new float[]
        {
            invS * LumaR + s, invS * LumaG,     invS * LumaB,     0, 0,
            invS * LumaR,     invS * LumaG + s, invS * LumaB,     0, 0,
            invS * LumaR,     invS * LumaG,     invS * LumaB + s, 0, 0,
            0,                0,                0,                1, 0
        };
    }

    /// <summary>Standard luma-preserving hue-rotation matrix (same formula as SVG's
    /// feColorMatrix type="hueRotate"), built from this file's own luma coefficients.</summary>
    private static float[] BuildHueRotation(double degrees)
    {
        if (degrees == 0) return Identity();

        float cosA = (float)Math.Cos(degrees * Math.PI / 180.0);
        float sinA = (float)Math.Sin(degrees * Math.PI / 180.0);

        return new float[]
        {
            LumaR + cosA * (1 - LumaR) - sinA * LumaR,      LumaG - cosA * LumaG - sinA * LumaG,      LumaB - cosA * LumaB + sinA * (1 - LumaB), 0, 0,
            LumaR - cosA * LumaR + sinA * 0.143f,            LumaG + cosA * (1 - LumaG) + sinA * 0.140f, LumaB - cosA * LumaB - sinA * 0.283f,      0, 0,
            LumaR - cosA * LumaR - sinA * (1 - LumaR),       LumaG - cosA * LumaG + sinA * LumaG,       LumaB + cosA * (1 - LumaB) + sinA * LumaB, 0, 0,
            0, 0, 0, 1, 0
        };
    }

    /// <summary>Warm (positive) / cool (negative) white-balance shift — boosts red and cuts
    /// blue for warmth, the reverse for cool.</summary>
    private static float[] BuildTemperature(double temperature)
    {
        if (temperature == 0) return Identity();
        float offset = (float)(temperature / 100.0 * 60);
        return new float[]
        {
            1, 0, 0, 0, offset,
            0, 1, 0, 0, 0,
            0, 0, 1, 0, -offset,
            0, 0, 0, 1, 0
        };
    }

    /// <summary>Green (positive) / magenta (negative) tint shift.</summary>
    private static float[] BuildTint(double tint)
    {
        if (tint == 0) return Identity();
        float offset = (float)(tint / 100.0 * 60);
        return new float[]
        {
            1, 0, 0, 0, 0,
            0, 1, 0, 0, offset,
            0, 0, 1, 0, 0,
            0, 0, 0, 1, 0
        };
    }

    /// <summary>Spatial (non-color-matrix) filter for blur/sharpen — composes an
    /// unsharp-mask sharpen (a second internal blur pass) on top of any user-requested blur.
    /// Returns null when both are at their default (no-op) values.</summary>
    public static SKImageFilter? BuildSpatialFilter(double blurRadius, double sharpenAmount)
    {
        SKImageFilter? userBlur = blurRadius > 0 ? SKImageFilter.CreateBlur((float)blurRadius, (float)blurRadius) : null;
        if (sharpenAmount <= 0) return userBlur; // caller owns and disposes this

        // userBlur/innerBlur are consumed into (retained by) the composed filter below, which
        // is what the caller actually owns and disposes — dispose our own references to the
        // intermediates now that they've been composed in, mirroring standard SkiaSharp usage
        // (a filter DAG's parent retains its own native reference to each input it composes).
        using (userBlur)
        {
            // Unsharp mask: sharpened = (1+amount)*original - amount*blur(original), where
            // "original" is whatever's already upstream (userBlur, or the literal source image
            // when userBlur is null — a null input/foreground means "use source" in Skia).
            using var innerBlur = SKImageFilter.CreateBlur(2f, 2f, userBlur!);
            return SKImageFilter.CreateArithmetic(0, (float)(1 + sharpenAmount), (float)(-sharpenAmount), 0,
                true, innerBlur, userBlur!);
        }
    }
}

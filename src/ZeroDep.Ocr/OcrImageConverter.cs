using System;
using ZeroDep.Abstractions;
using ZeroDep.Filters;

namespace ZeroDep.Ocr;

/// <summary>
/// Bridges ZeroDep's pure-BCL image decoders to the OCR layer, turning a decoded raster (or a raw
/// JPEG stream) into a <see cref="DecodedImage"/> an <see cref="IOcrEngine"/> can recognize.
/// </summary>
public static class OcrImageConverter
{
    /// <summary>Decodes a JPEG (<c>/DCTDecode</c>) byte stream into a <see cref="DecodedImage"/>.</summary>
    /// <param name="jpeg">The raw JPEG bytes.</param>
    /// <param name="dpi">The effective resolution the image is placed at, where known; 0 if unknown.</param>
    public static DecodedImage FromJpeg(byte[] jpeg, int dpi = 0)
        => FromRaster(JpegDecoder.Decode(jpeg), dpi);

    /// <summary>
    /// Decodes a CCITT (<c>/CCITTFaxDecode</c>) bi-level byte stream into a grayscale
    /// <see cref="DecodedImage"/>. Group 4 (<c>K &lt; 0</c>) and Group 3 (<c>K &gt;= 0</c>) are supported.
    /// </summary>
    /// <param name="data">The raw CCITT codestream bytes.</param>
    /// <param name="parms">The CCITT decode parameters captured from the image's <c>/DecodeParms</c>.</param>
    /// <param name="dpi">The effective resolution the image is placed at, where known; 0 if unknown.</param>
    public static DecodedImage FromCcitt(byte[] data, CcittParameters parms, int dpi = 0)
    {
        if (parms is null)
        {
            throw new ArgumentNullException(nameof(parms));
        }

        RasterImage raster = CcittFaxDecode.Decode(data, new CcittParams
        {
            K = parms.K,
            Columns = parms.Columns,
            Rows = parms.Rows,
            BlackIs1 = parms.BlackIs1,
            EncodedByteAlign = parms.EncodedByteAlign,
        });

        return FromRaster(raster, dpi);
    }

    /// <summary>Converts a decoded <see cref="RasterImage"/> into a <see cref="DecodedImage"/>.</summary>
    /// <param name="raster">The decoded raster image (1 = grayscale, 3 = RGB).</param>
    /// <param name="dpi">The effective resolution the image is placed at, where known; 0 if unknown.</param>
    public static DecodedImage FromRaster(RasterImage raster, int dpi = 0)
    {
        if (raster is null)
        {
            throw new ArgumentNullException(nameof(raster));
        }

        PixelFormat format = raster.Components == 1 ? PixelFormat.Gray8 : PixelFormat.Rgb24;
        return new DecodedImage
        {
            Width = raster.Width,
            Height = raster.Height,
            Dpi = dpi,
            Format = format,
            Pixels = raster.Samples,
        };
    }
}

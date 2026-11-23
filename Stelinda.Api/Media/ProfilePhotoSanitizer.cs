using System.Buffers.Binary;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Stelinda.Api.Media;

/// <summary>
/// Profil fotoğrafını saklamadan önce temizler. Tür dosyanın ilk
/// baytlarından anlaşılır (istemcinin söylediği tür ve dosya adı yok
/// sayılır); yalnız JPEG ve PNG kabul edilir.
///
/// Fotoğrafın içindeki üst veri silinir: iPhone fotoğrafı çekildiği yerin
/// konumunu (GPS) EXIF'te taşır ve profil fotoğrafını ligdeki başka
/// kullanıcılar da indirir. Resim yeniden kodlanmaz, yalnız üst veri
/// parçaları atılır; piksel verisi aynen kalır.
///
/// Atma bir izin listesiyle yapılır, tanınmayan parça atılır. JPEG'de JFIF,
/// renk profili (ICC) ve Adobe parçası kalır; EXIF'ten yalnız yön
/// (Orientation) yeniden yazılır, yoksa dikey çekilmiş fotoğraf yan döner.
/// Bitiş işaretinden sonrası (ikincil resimler) atılır. Yapısı bozuk dosya
/// reddedilir.
/// </summary>
public static class ProfilePhotoSanitizer
{
    public sealed record Result(byte[] Bytes, string Extension, string ContentType);

    /// <summary>Yüklenen dosyayı okuyup temizler; tanınmazsa null.</summary>
    public static async Task<Result?> SanitizeAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream((int)Math.Min(file.Length, int.MaxValue));
        await file.CopyToAsync(buffer, cancellationToken);
        return Sanitize(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    public static Result? Sanitize(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            var jpeg = SanitizeJpeg(data);
            return jpeg is null ? null : new Result(jpeg, ".jpg", "image/jpeg");
        }
        if (data.StartsWith(PngSignature))
        {
            var png = SanitizePng(data);
            return png is null ? null : new Result(png, ".png", "image/png");
        }
        return null;
    }

    // ---------------------------------------------------------------- JPEG

    private static byte[]? SanitizeJpeg(ReadOnlySpan<byte> src)
    {
        using var body = new MemoryStream(src.Length);
        ushort orientation = 1;
        var sawFrame = false;
        var sawScan = false;
        var pos = 2; // SOI

        while (true)
        {
            if (pos >= src.Length || src[pos] != 0xFF) return null;
            while (pos < src.Length && src[pos] == 0xFF) pos++; // dolgu baytları
            if (pos >= src.Length) return null;
            var marker = src[pos++];

            if (marker == 0xD9) // EOI
            {
                if (!sawScan) return null;
                body.Write([0xFF, 0xD9]);
                break;
            }
            // Uzunluğu olmayan işaretler yalnız sıkıştırılmış verinin içinde olur.
            if (marker is 0x00 or 0x01 or 0xD8 or (>= 0xD0 and <= 0xD7)) return null;

            if (pos + 2 > src.Length) return null;
            int length = BinaryPrimitives.ReadUInt16BigEndian(src[pos..]);
            if (length < 2 || pos + length > src.Length) return null;
            var payload = src.Slice(pos + 2, length - 2);

            if (marker == 0xE1 && payload.StartsWith("Exif\0\0"u8))
                orientation = ReadOrientation(payload[6..]) ?? orientation;

            if (KeepJpegSegment(marker, payload))
            {
                body.Write([0xFF, marker]);
                body.Write(src.Slice(pos, length));
            }
            pos += length;

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                sawFrame = true;

            if (marker == 0xDA) // SOS: sıkıştırılmış veri sıradaki işarete kadar sürer
            {
                if (!sawFrame) return null;
                sawScan = true;
                var start = pos;
                while (true)
                {
                    if (pos + 1 >= src.Length) return null;
                    if (src[pos] == 0xFF)
                    {
                        var next = src[pos + 1];
                        if (next == 0x00 || next is >= 0xD0 and <= 0xD7)
                        {
                            pos += 2;
                            continue;
                        }
                        break;
                    }
                    pos++;
                }
                body.Write(src[start..pos]);
            }
        }

        using var output = new MemoryStream((int)body.Length + 64);
        output.Write([0xFF, 0xD8]);
        if (orientation != 1) WriteOrientationExif(output, orientation);
        body.Position = 0;
        body.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>
    /// Kalan parçalar: resmi çözmeye yarayan her şey (tablolar, çerçeve,
    /// tarama), JFIF, renk profili ve Adobe. Öbür APPn (EXIF, XMP, IPTC,
    /// MPF, C2PA…) ve yorum atılır.
    /// </summary>
    private static bool KeepJpegSegment(byte marker, ReadOnlySpan<byte> payload) => marker switch
    {
        0xE0 => true,
        0xE2 => payload.StartsWith("ICC_PROFILE\0"u8),
        0xEE => true,
        >= 0xE1 and <= 0xEF => false,
        0xFE => false,
        _ => true,
    };

    /// <summary>EXIF'in ilk dizinindeki yön etiketi (0x0112); yoksa null.</summary>
    private static ushort? ReadOrientation(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8) return null;
        bool little;
        if (tiff[0] == (byte)'I' && tiff[1] == (byte)'I') little = true;
        else if (tiff[0] == (byte)'M' && tiff[1] == (byte)'M') little = false;
        else return null;

        var ifd = ReadU32(tiff, 4, little);
        if (ifd > (uint)tiff.Length - 2) return null;
        var start = (int)ifd;
        var count = ReadU16(tiff, start, little);
        for (var i = 0; i < count; i++)
        {
            var entry = start + 2 + i * 12;
            if (entry + 12 > tiff.Length) return null;
            if (ReadU16(tiff, entry, little) != 0x0112) continue;
            if (ReadU16(tiff, entry + 2, little) != 3) return null; // SHORT olmalı
            var value = ReadU16(tiff, entry + 8, little);
            return value is >= 1 and <= 8 ? value : null;
        }
        return null;
    }

    private static ushort ReadU16(ReadOnlySpan<byte> s, int offset, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(s[offset..]) : BinaryPrimitives.ReadUInt16BigEndian(s[offset..]);

    private static uint ReadU32(ReadOnlySpan<byte> s, int offset, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(s[offset..]) : BinaryPrimitives.ReadUInt32BigEndian(s[offset..]);

    /// <summary>Yalnız yön etiketi taşıyan en küçük EXIF parçası (büyük uçlu).</summary>
    private static void WriteOrientationExif(Stream output, ushort orientation)
    {
        ReadOnlySpan<byte> head =
        [
            0xFF, 0xE1, 0x00, 0x22,                       // APP1, uzunluk 34
            0x45, 0x78, 0x69, 0x66, 0x00, 0x00,           // "Exif\0\0"
            0x4D, 0x4D, 0x00, 0x2A,                       // TIFF, büyük uçlu
            0x00, 0x00, 0x00, 0x08,                       // ilk dizin 8. baytta
            0x00, 0x01,                                   // tek etiket
            0x01, 0x12, 0x00, 0x03,                       // Orientation, SHORT
            0x00, 0x00, 0x00, 0x01,                       // bir değer
        ];
        output.Write(head);
        output.Write([(byte)(orientation >> 8), (byte)orientation, 0x00, 0x00]);
        output.Write([0x00, 0x00, 0x00, 0x00]); // sonraki dizin yok
    }

    // ----------------------------------------------------------------- PNG

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Kalan parçalar: resim, saydamlık, renk ve animasyon. Metin (tEXt,
    /// iTXt, zTXt; XMP burada durur), EXIF, zaman ve tanınmayanlar atılır.
    /// </summary>
    private static readonly HashSet<string> KeptPngChunks =
    [
        "IHDR", "PLTE", "IDAT", "IEND", "tRNS", "cHRM", "gAMA", "iCCP", "sBIT", "sRGB",
        "cICP", "mDCV", "mDCv", "cLLI", "cLLi", "pHYs", "bKGD", "hIST", "sPLT",
        "acTL", "fcTL", "fdAT",
    ];

    private static byte[]? SanitizePng(ReadOnlySpan<byte> src)
    {
        using var output = new MemoryStream(src.Length);
        output.Write(PngSignature);
        var pos = PngSignature.Length;
        var first = true;
        while (pos + 12 <= src.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(src[pos..]);
            if (length > (uint)(src.Length - pos - 12)) return null;
            var typeBytes = src.Slice(pos + 4, 4);
            foreach (var b in typeBytes)
            {
                if (b is not (>= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z')) return null;
            }
            var type = Encoding.ASCII.GetString(typeBytes);
            if (first && type != "IHDR") return null;
            first = false;

            var total = 12 + (int)length;
            if (KeptPngChunks.Contains(type)) output.Write(src.Slice(pos, total));
            pos += total;
            if (type == "IEND") return output.ToArray();
        }
        return null; // IEND yok: dosya yarım
    }
}

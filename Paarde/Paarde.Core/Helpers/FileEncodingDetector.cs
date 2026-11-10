using System.Text;

using Paarde.Core.Models;

namespace Paarde.Core.Helpers;

// Detects the encoding and line-ending convention an existing file is actually using on disk, so
// FileChangeApplyService.Apply can write back in the same format instead of always normalizing to
// UTF-8 (no BOM) with LF line endings. Deliberately mirrors the BOM detection built into
// File.ReadAllText (which FileContentReader relies on for OriginalContent) so the two agree about
// what encoding a given file is in.
public static class FileEncodingDetector
{
    public static FileEncodingInfo Detect(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            return FileEncodingInfo.Default;
        }

        try
        {
            var bytes = File.ReadAllBytes(fullPath);
            var encoding = DetectEncoding(bytes);
            var newLine = DetectNewLine(encoding.GetString(bytes));

            return new FileEncodingInfo(encoding, newLine);
        }
        catch (IOException)
        {
            return FileEncodingInfo.Default;
        }
        catch (UnauthorizedAccessException)
        {
            return FileEncodingInfo.Default;
        }
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        // Checked before the 2-byte UTF-16 BOMs below, since "FF FE" is also the first two bytes of
        // the 4-byte UTF-32 LE BOM ("FF FE 00 00").
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            return new UTF32Encoding(bigEndian: false, byteOrderMark: true);
        }

        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode; // UTF-16 LE
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode; // UTF-16 BE
        }

        // No recognized BOM - matches what File.ReadAllText already assumed for this file.
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    // A single CRLF anywhere is treated as "this file is CRLF" rather than requiring every line to
    // agree - real files occasionally have one straggler line, and preserving the dominant
    // convention beats leaving the file mixed.
    private static string? DetectNewLine(string content)
    {
        if (content.Contains("\r\n", StringComparison.Ordinal))
        {
            return "\r\n";
        }

        return content.Contains('\n') ? "\n" : null;
    }
}

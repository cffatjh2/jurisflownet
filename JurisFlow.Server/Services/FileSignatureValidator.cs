namespace JurisFlow.Server.Services
{
    public static class FileSignatureValidator
    {
        public static bool IsValidAvatarImage(string mimeType, ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0)
            {
                return false;
            }

            return mimeType.Trim().ToLowerInvariant() switch
            {
                "image/png" => IsPng(bytes),
                "image/jpeg" => IsJpeg(bytes),
                "image/webp" => IsWebp(bytes),
                "image/gif" => IsGif(bytes),
                _ => false
            };
        }

        public static bool IsValidMessageAttachment(string mimeType, ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0)
            {
                return false;
            }

            return mimeType.Trim().ToLowerInvariant() switch
            {
                "application/pdf" => bytes.StartsWith("%PDF"u8),
                "image/png" => IsPng(bytes),
                "image/jpeg" => IsJpeg(bytes),
                "image/webp" => IsWebp(bytes),
                "application/msword" or "application/vnd.ms-excel" or "application/vnd.ms-powerpoint" => IsCompoundOffice(bytes),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                    or "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                    or "application/vnd.openxmlformats-officedocument.presentationml.presentation" => IsZipContainer(bytes),
                "text/plain" => !bytes.Contains((byte)0),
                _ => false
            };
        }

        private static bool IsPng(ReadOnlySpan<byte> bytes)
        {
            return bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        }

        private static bool IsJpeg(ReadOnlySpan<byte> bytes)
        {
            return bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF });
        }

        private static bool IsWebp(ReadOnlySpan<byte> bytes)
        {
            return bytes.Length >= 12 &&
                   bytes[..4].StartsWith("RIFF"u8) &&
                   bytes[8..12].StartsWith("WEBP"u8);
        }

        private static bool IsGif(ReadOnlySpan<byte> bytes)
        {
            return bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8);
        }

        private static bool IsCompoundOffice(ReadOnlySpan<byte> bytes)
        {
            return bytes.StartsWith(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 });
        }

        private static bool IsZipContainer(ReadOnlySpan<byte> bytes)
        {
            return bytes.StartsWith(new byte[] { 0x50, 0x4B, 0x03, 0x04 }) ||
                   bytes.StartsWith(new byte[] { 0x50, 0x4B, 0x05, 0x06 }) ||
                   bytes.StartsWith(new byte[] { 0x50, 0x4B, 0x07, 0x08 });
        }
    }
}

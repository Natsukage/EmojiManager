using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace EmojiManager
{
    /// <summary>
    /// 图像格式检测工具类
    /// 用于检测文件的实际图像格式，而不依赖文件扩展名
    /// </summary>
    public static class ImageFormatDetector
    {
        /// <summary>
        /// 格式到扩展名的映射
        /// </summary>
        private static readonly Dictionary<System.Drawing.Imaging.ImageFormat, string> FormatToExtension = new()
        {
            { System.Drawing.Imaging.ImageFormat.Jpeg, "jpg" },
            { System.Drawing.Imaging.ImageFormat.Png, "png" },
            { System.Drawing.Imaging.ImageFormat.Gif, "gif" },
            { System.Drawing.Imaging.ImageFormat.Bmp, "bmp" },
            { System.Drawing.Imaging.ImageFormat.Tiff, "tiff" },
            { System.Drawing.Imaging.ImageFormat.Emf, "emf" },
            { System.Drawing.Imaging.ImageFormat.Wmf, "wmf" },
            { System.Drawing.Imaging.ImageFormat.Icon, "ico" }
        };

        /// <summary>
        /// 支持的图片格式扩展名
        /// </summary>
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            "jpg", "jpeg", "png", "gif", "bmp", "webp", "tiff", "ico"
        };

        /// <summary>
        /// 检测图像文件的实际格式
        /// </summary>
        /// <param name="fileBytes">文件字节数据</param>
        /// <returns>实际的文件扩展名，如果不是图像文件则返回null</returns>
        public static string? DetectImageFormat(byte[]? fileBytes)
        {
            if (fileBytes == null || fileBytes.Length == 0)
                return null;

            try
            {
                // 首先通过文件头快速识别常见格式
                var quickDetected = DetectByFileHeader(fileBytes);
                if (quickDetected != null)
                    return quickDetected;

                // 如果快速检测失败，尝试使用System.Drawing
                using var stream = new MemoryStream(fileBytes);
                using var image = Image.FromStream(stream);
                
                if (FormatToExtension.TryGetValue(image.RawFormat, out var extension))
                {
                    return extension;
                }
            }
            catch
            {
                // 如果System.Drawing也失败了，返回null
            }

            return null;
        }

        /// <summary>
        /// 通过文件头快速检测图像格式
        /// </summary>
        /// <param name="fileBytes">文件字节数据</param>
        /// <returns>检测到的扩展名，如果无法识别则返回null</returns>
        private static string? DetectByFileHeader(byte[] fileBytes)
        {
            if (fileBytes.Length < 8)
                return null;

            // JPEG
            if (fileBytes[0] == 0xFF && fileBytes[1] == 0xD8)
                return "jpg";

            // PNG
            if (fileBytes[0] == 0x89 && fileBytes[1] == 0x50 && fileBytes[2] == 0x4E && fileBytes[3] == 0x47)
                return "png";

            // GIF
            if (fileBytes[0] == 0x47 && fileBytes[1] == 0x49 && fileBytes[2] == 0x46)
                return "gif";

            // BMP
            if (fileBytes[0] == 0x42 && fileBytes[1] == 0x4D)
                return "bmp";

            // WebP
            if (fileBytes.Length >= 12 && 
                fileBytes[0] == 0x52 && fileBytes[1] == 0x49 && fileBytes[2] == 0x46 && fileBytes[3] == 0x46 &&
                fileBytes[8] == 0x57 && fileBytes[9] == 0x45 && fileBytes[10] == 0x42 && fileBytes[11] == 0x50)
                return "webp";

            // TIFF (little endian)
            if (fileBytes[0] == 0x49 && fileBytes[1] == 0x49 && fileBytes[2] == 0x2A && fileBytes[3] == 0x00)
                return "tiff";

            // TIFF (big endian)
            if (fileBytes[0] == 0x4D && fileBytes[1] == 0x4D && fileBytes[2] == 0x00 && fileBytes[3] == 0x2A)
                return "tiff";

            // ICO
            if (fileBytes[0] == 0x00 && fileBytes[1] == 0x00 && fileBytes[2] == 0x01 && fileBytes[3] == 0x00)
                return "ico";

            return null;
        }

        /// <summary>
        /// 检查扩展名是否为支持的图片格式
        /// </summary>
        /// <param name="extension">扩展名（不含点号）</param>
        /// <returns>是否支持</returns>
        public static bool IsSupportedImageFormat(string extension)
        {
            return SupportedExtensions.Contains(extension?.TrimStart('.') ?? "");
        }

        /// <summary>
        /// 获取所有支持的图片格式扩展名
        /// </summary>
        /// <returns>支持的扩展名列表</returns>
        public static IReadOnlySet<string> GetSupportedExtensions()
        {
            return SupportedExtensions;
        }
    }
} 
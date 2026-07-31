using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ReIdFaceBox.Models;

namespace ReIdFaceBox.Services;

public static class ImageRenderer
{
    /// <summary>
    /// 从内存中的图片字节绘制检测框，返回结果 Bitmap
    /// </summary>
    public static Bitmap RenderDetections(byte[] imageBytes, List<PersonDetection> detections, string? overlayText = null)
    {
        using var ms = new MemoryStream(imageBytes);
        var bitmap = new Bitmap(ms);

        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;

        // 无检测结果且无文字时直接返回原图
        if (detections.Count == 0 && string.IsNullOrEmpty(overlayText))
            return bitmap;

        var renderTarget = new RenderTargetBitmap(new PixelSize(width, height));

        using (var ctx = renderTarget.CreateDrawingContext())
        {
            ctx.DrawImage(bitmap, new Rect(0, 0, width, height));

            var yellowPen = new Pen(Brushes.Yellow, 1);
            var redPen = new Pen(Brushes.Red, 1);

            foreach (var person in detections)
            {
                // 画人物框 - 黄色 1px
                if (person.Bbox is { Width: > 0, Height: > 0 })
                {
                    ctx.DrawRectangle(null, yellowPen,
                        new Rect(person.Bbox.X, person.Bbox.Y, person.Bbox.Width, person.Bbox.Height));

                    // 在 bbox 上方写置信度
                    var confText = new FormattedText(
                        $"{person.Confidence}",
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Arial"),
                        Math.Max(12, width / 80.0),
                        Brushes.Yellow);
                    var textY = person.Bbox.Y - confText.Height - 2;
                    if (textY < 0) textY = person.Bbox.Y + 2;
                    var confBg = new Rect(person.Bbox.X, textY, confText.Width + 4, confText.Height + 2);
                    ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), null, confBg);
                    ctx.DrawText(confText, new Point(person.Bbox.X + 2, textY));
                }

                // 画人脸框 - 红色 1px
                if (person.Face?.Bbox is { Width: > 0, Height: > 0 })
                {
                    ctx.DrawRectangle(null, redPen,
                        new Rect(person.Face.Bbox.X, person.Face.Bbox.Y, person.Face.Bbox.Width, person.Face.Bbox.Height));

                    // 在 face bbox 上方写置信度
                    var faceConfText = new FormattedText(
                        $"{person.Face.Confidence}",
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Arial"),
                        Math.Max(12, width / 80.0),
                        Brushes.Red);
                    var faceTextY = person.Face.Bbox.Y - faceConfText.Height - 2;
                    if (faceTextY < 0) faceTextY = person.Face.Bbox.Y + 2;
                    var faceBg = new Rect(person.Face.Bbox.X, faceTextY, faceConfText.Width + 4, faceConfText.Height + 2);
                    ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), null, faceBg);
                    ctx.DrawText(faceConfText, new Point(person.Face.Bbox.X + 2, faceTextY));
                }
            }

            // 在左上角绘制对比结果文字
            if (!string.IsNullOrEmpty(overlayText))
            {
                var fontSize = Math.Max(14, width / 60.0); // 根据图片大小自适应字号
                var typeface = new Typeface("Arial");
                var lines = overlayText.Split('\n');
                double y = 44;

                foreach (var line in lines)
                {
                    var formattedText = new FormattedText(
                        line,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        Brushes.Lime);

                    // 先画半透明黑色背景让文字可读
                    var textBounds = new Rect(2, y, formattedText.Width + 6, formattedText.Height + 2);
                    ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), null, textBounds);

                    ctx.DrawText(formattedText, new Point(5, y));
                    y += formattedText.Height + 2;
                }
            }
        }

        bitmap.Dispose();
        return renderTarget;
    }
}

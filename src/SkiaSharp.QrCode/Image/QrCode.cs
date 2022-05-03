using System.IO;

namespace SkiaSharp.QrCode.Image
{
    public class QrCode
    {
        private readonly string content;
        private readonly SKImageInfo qrInfo;
        private readonly SKEncodedImageFormat outputFormat = SKEncodedImageFormat.Png;
        private readonly int quality = 100;

        public QrCode(string content, Vector2Slim qrSize)
            => (this.content, this.qrInfo) = (content, new SKImageInfo(qrSize.X, qrSize.Y));
        public QrCode(string content, Vector2Slim qrSize, SKEncodedImageFormat outputFormat) : this(content, qrSize)
            => this.outputFormat = outputFormat;
        public QrCode(string content, Vector2Slim qrSize, SKEncodedImageFormat outputFormat, int quality) : this(content, qrSize, outputFormat)
            => this.quality = quality;

        /// <summary>
        /// Generate QR Code and output to stream
        /// </summary>
        /// <param name="outputImage"></param>
        public void GenerateImage(Stream outputImage, bool resetStreamPosition = true, ECCLevel eccLevel = ECCLevel.L)
        {
            if (outputImage.CanSeek && resetStreamPosition)
                outputImage.Seek(0, SeekOrigin.Begin);

            using (var generator = new QRCodeGenerator())
            {
                var qr = generator.CreateQrCode(content, eccLevel);

                using (var qrSurface = SKSurface.Create(qrInfo))
                {
                    var qrCanvas = qrSurface.Canvas;
                    qrCanvas.Render(qr, qrInfo.Width, qrInfo.Height);

                    using (var qrImage = qrSurface.Snapshot())
                    {
                        Save(qrImage, outputImage);
                    }
                }
            }
        }

        /// <summary>
        /// Generate QR Code and conbine with base image, then output to stream
        /// </summary>
        /// <param name="outputImage"></param>
        /// <param name="baseImage"></param>
        /// <param name="baseQrSize"></param>
        /// <param name="qrPosition"></param>
        public void GenerateImage(Stream outputImage, Stream baseImage, Vector2Slim baseQrSize, Vector2Slim qrPosition, bool resetStreamPosition = true, ECCLevel eccLevel = ECCLevel.L)
        {
            if (outputImage.CanSeek && resetStreamPosition)
                outputImage.Seek(0, SeekOrigin.Begin);
            if (baseImage.CanSeek && resetStreamPosition)
                baseImage.Seek(0, SeekOrigin.Begin);

            using (var generator = new QRCodeGenerator())
            {
                var qr = generator.CreateQrCode(content, eccLevel);

                using (var qrSurface = SKSurface.Create(qrInfo))
                {
                    var qrCanvas = qrSurface.Canvas;
                    qrCanvas.Render(qr, qrInfo.Width, qrInfo.Height);

                    using (var qrImage = qrSurface.Snapshot())
                    {
                        SaveCombinedImage(qrImage, baseImage, baseQrSize, qrPosition, outputImage);
                    }
                }
            }
        }
        /// <summary>
        /// Generate QR Code and conbine with base image, then output to stream
        /// </summary>
        /// <param name="outputImage"></param>
        /// <param name="baseImage"></param>
        /// <param name="baseQrSize"></param>
        /// <param name="qrPosition"></param>
        public void GenerateImage(Stream outputImage, byte[] baseImage, Vector2Slim baseQrSize, Vector2Slim qrPosition, bool resetStreamPosition = true, ECCLevel eccLevel = ECCLevel.L)
        {
            if (outputImage.CanSeek && resetStreamPosition)
                outputImage.Seek(0, SeekOrigin.Begin);

            using (var generator = new QRCodeGenerator())
            {
                var qr = generator.CreateQrCode(content, eccLevel);

                using (var qrSurface = SKSurface.Create(qrInfo))
                {
                    var qrCanvas = qrSurface.Canvas;
                    qrCanvas.Render(qr, qrInfo.Width, qrInfo.Height);

                    using (var qrImage = qrSurface.Snapshot())
                    {
                        SaveCombinedImage(qrImage, baseImage, baseQrSize, qrPosition, outputImage);
                    }
                }
            }
        }

        private void Save(SKImage qrImage, Stream outputImage)
        {
            using (var data = qrImage.Encode(outputFormat, quality))
            {
                data.SaveTo(outputImage);
            }
        }

        private void SaveCombinedImage(SKImage qrImage, Stream baseImage, Vector2Slim baseImageSize, Vector2Slim qrPosition, Stream output)
        {
            var baseInfo = new SKImageInfo(baseImageSize.X, baseImageSize.Y);
            using (var baseSurface = SKSurface.Create(baseInfo))
            using (SKBitmap baseBitmap = SKBitmap.Decode(baseImage))
            {
                // combine with base image
                var baseCanvas = baseSurface.Canvas;
                baseCanvas.DrawBitmap(baseBitmap, 0, 0);
                baseCanvas.DrawImage(qrImage, qrPosition.X, qrPosition.Y);

                using (var image = baseSurface.Snapshot())
                using (var data = image.Encode(outputFormat, quality))
                {
                    data.SaveTo(output);
                }
            }
        }

        private void SaveCombinedImage(SKImage qrImage, byte[] baseImage, Vector2Slim baseImageSize, Vector2Slim qrPosition, Stream output)
        {
            var baseInfo = new SKImageInfo(baseImageSize.X, baseImageSize.Y);
            using (var baseSurface = SKSurface.Create(baseInfo))
            using (SKBitmap baseBitmap = SKBitmap.Decode(baseImage))
            {
                // combine with base image
                var baseCanvas = baseSurface.Canvas;
                baseCanvas.DrawBitmap(baseBitmap, 0, 0);
                baseCanvas.DrawImage(qrImage, qrPosition.X, qrPosition.Y);

                using (var image = baseSurface.Snapshot())
                using (var data = image.Encode(outputFormat, quality))
                {
                    data.SaveTo(output);
                }
            }
        }
    }
}

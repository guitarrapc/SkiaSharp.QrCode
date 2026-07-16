namespace SkiaSharp.QrCode.Tests;

public class MicroQrCodeDataUnitTest
{
    [Test]
    public async Task GetRawData_RoundTripsThroughConstructor()
    {
        var original = MicroQrCodeGenerator.CreateMicroQrCode("01234567", MicroQrEccLevel.L, quietZoneSize: 2);
        var raw = original.GetRawData();

        var restored = new MicroQrCodeData(raw, quietZoneSize: 2);

        await Assert.That(restored.Version).IsEqualTo(original.Version);
        await Assert.That(restored.Size).IsEqualTo(original.Size);
        for (var row = 0; row < original.Size; row++)
        {
            for (var col = 0; col < original.Size; col++)
            {
                if (original[row, col] != restored[row, col])
                {
                    Assert.Fail($"Module mismatch at ({row},{col})");
                }
            }
        }
    }

    [Test]
    public async Task GetRawData_HeaderIsQrxWithSymbolTypeAndDimensions()
    {
        var data = MicroQrCodeGenerator.CreateMicroQrCode("12345", MicroQrEccLevel.ErrorDetectionOnly);
        var raw = data.GetRawData();

        await Assert.That(raw.Length).IsEqualTo(data.GetRawDataSize());
        // "QRX" magic, symbol type 1 (Micro QR), width, height, then packed core bits.
        await Assert.That(raw[0]).IsEqualTo((byte)0x51);
        await Assert.That(raw[1]).IsEqualTo((byte)0x52);
        await Assert.That(raw[2]).IsEqualTo((byte)0x58);
        await Assert.That(raw[3]).IsEqualTo((byte)1);
        await Assert.That(raw[4]).IsEqualTo((byte)11);
        await Assert.That(raw[5]).IsEqualTo((byte)11);
        await Assert.That(raw.Length).IsEqualTo(6 + (11 * 11 + 7) / 8);
    }

    [Test]
    public async Task Constructor_RejectsInvalidHeader()
    {
        var valid = MicroQrCodeGenerator.CreateMicroQrCode("123", MicroQrEccLevel.L).GetRawData();

        var badMagic = (byte[])valid.Clone();
        badMagic[2] = (byte)'R';
        await Assert.That(() => new MicroQrCodeData(badMagic, 0)).Throws<InvalidDataException>();

        var badSymbolType = (byte[])valid.Clone();
        badSymbolType[3] = 9;
        await Assert.That(() => new MicroQrCodeData(badSymbolType, 0)).Throws<InvalidDataException>();

        var badSize = (byte[])valid.Clone();
        badSize[4] = 12; // not a Micro QR size (11/13/15/17)
        badSize[5] = 12;
        await Assert.That(() => new MicroQrCodeData(badSize, 0)).Throws<InvalidDataException>();

        var truncated = valid.AsSpan(0, valid.Length - 1).ToArray();
        await Assert.That(() => new MicroQrCodeData(truncated, 0)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Indexer_QuietZoneReadsLightAndOutOfRangeThrows()
    {
        var data = MicroQrCodeGenerator.CreateMicroQrCode("123", MicroQrEccLevel.L, quietZoneSize: 3);

        await Assert.That(data.Size).IsEqualTo(13 + 6);
        await Assert.That(data[0, 0]).IsFalse();          // quiet zone
        await Assert.That(data[3, 3]).IsTrue();           // core (0,0) = finder corner
        await Assert.That(() => data[19, 0]).Throws<IndexOutOfRangeException>();
    }
}

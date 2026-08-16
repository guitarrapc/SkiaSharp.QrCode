using SkiaSharp.QrCode.Internals.RmQr;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// <see cref="RmQRModulePlacer.PlaceSymbol"/> (fast path: cached per-version tables,
/// vector bit expansion, pair stores + scatter) versus
/// <see cref="RmQRModulePlacer.PlaceSymbolReference"/> (per-module painters), byte for
/// byte, for every version × ECC level over all-zero, all-one, pseudo-random and
/// over-long messages, on a dirty core (every module must be written), plus the
/// undersized-buffer contracts of the fast path.
/// </summary>
public class RmQRModulePlacerParityTest
{
    public static IEnumerable<(RmQRVersion version, RmQREccLevel ecc)> AllVersionEcc()
    {
        foreach (var v in Enum.GetValues<RmQRVersion>())
        {
            yield return (v, RmQREccLevel.M);
            yield return (v, RmQREccLevel.H);
        }
    }

    private static byte[] PseudoRandom(int length, int seed)
    {
        var bytes = new byte[length];
        var state = (uint)seed * 2654435761u + 7u;
        for (var i = 0; i < length; i++)
        {
            state = state * 1664525u + 1013904223u;
            bytes[i] = (byte)(state >> 16);
        }
        return bytes;
    }

    [Test]
    [MethodDataSource(nameof(AllVersionEcc))]
    public async Task FastPath_MatchesReference_EveryMessageShape(RmQRVersion version, RmQREccLevel ecc)
    {
        var total = RmQRConstants.GetTotalCodewordCount(version);
        var size = RmQRConstants.GetWidth(version) * RmQRConstants.GetHeight(version);
        var messages = new[]
        {
            new byte[total],
            Enumerable.Repeat((byte)0xFF, total).ToArray(),
            PseudoRandom(total, (int)version * 31 + (int)ecc),
            PseudoRandom(total, (int)version * 17 + (int)ecc + 5),
            PseudoRandom(total + 3, (int)version + (int)ecc), // over-long: only the first `total` codewords count
            PseudoRandom(RmQRCodewordEncoder.GetFinalMessageSize(version), 99 + (int)version), // the generator's buffer size
        };
        foreach (var message in messages)
        {
            var expected = new byte[size];
            expected.AsSpan().Fill(0xA5);
            RmQRModulePlacer.PlaceSymbolReference(expected, version, ecc, message);

            var actual = new byte[size + 3]; // oversized core: only the first w×h bytes are written
            actual.AsSpan().Fill(0xA5);
            RmQRModulePlacer.PlaceSymbol(actual, version, ecc, message);

            if (!actual.AsSpan(0, size).SequenceEqual(expected))
            {
                var first = 0;
                while (first < size && actual[first] == expected[first]) first++;
                var width = RmQRConstants.GetWidth(version);
                Assert.Fail($"{version}-{ecc} msg[0]={message[0]:X2}: first mismatch at {first} (row {first / width}, col {first % width}): expected {expected[first]}, actual {actual[first]}");
            }
            await Assert.That(actual.AsSpan(size).ToArray()).IsEquivalentTo(new byte[] { 0xA5, 0xA5, 0xA5 });
        }
    }

    [Test]
    public async Task FastPath_RejectsUndersizedBuffers_LikeReference()
    {
        var message = new byte[RmQRCodewordEncoder.GetFinalMessageSize(RmQRVersion.R17x139)];
        await Assert.That(() => RmQRModulePlacer.PlaceSymbol(new byte[17 * 139 - 1], RmQRVersion.R17x139, RmQREccLevel.M, message)).Throws<ArgumentException>();
        await Assert.That(() => RmQRModulePlacer.PlaceSymbol(new byte[17 * 139], RmQRVersion.R17x139, RmQREccLevel.M, message.AsSpan(0, RmQRConstants.GetTotalCodewordCount(RmQRVersion.R17x139) - 1).ToArray())).Throws<ArgumentException>();
        await Assert.That(() => RmQRModulePlacer.PlaceSymbolReference(new byte[17 * 139 - 1], RmQRVersion.R17x139, RmQREccLevel.M, message)).Throws<ArgumentException>();
    }

    [Test]
    public async Task FastPath_IsRepeatable_AcrossEccAndVersionsSharingTables()
    {
        // the per-version cache serves both ECC levels and repeated calls; alternate them
        var v = RmQRVersion.R11x59;
        var total = RmQRConstants.GetTotalCodewordCount(v);
        var size = RmQRConstants.GetWidth(v) * RmQRConstants.GetHeight(v);
        for (var i = 0; i < 6; i++)
        {
            var ecc = (i & 1) == 0 ? RmQREccLevel.M : RmQREccLevel.H;
            var message = PseudoRandom(total, i);
            var expected = new byte[size];
            RmQRModulePlacer.PlaceSymbolReference(expected, v, ecc, message);
            var actual = new byte[size];
            RmQRModulePlacer.PlaceSymbol(actual, v, ecc, message);
            await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();
        }
    }
}

/// <summary>
/// What <c>RmQRSegmentation.Optimal</c> costs against <c>Single</c>, encoding the same
/// content through the same zero-allocation span path so each pair differs only in the
/// segmentation decision.
///
/// This is a separate class from <see cref="RmQREncodeEndToEnd"/> on purpose. That one
/// varies version and mode with the version pinned, and ranks everything against one
/// baseline row; this one varies content shape with the version free, and every row is
/// meaningless without its same-run partner. Mixing them would make both tables harder
/// to read and both filters slower to run.
///
/// The shapes are chosen to separate the two things that drive planning cost:
///
///   Length, shape held fixed   : mixed-20 / -60 / -120 / -150 (half letters, half digits)
///   Shape, length held at 120  : numeric / alnum / byte / mixed / alt1 / alt10
///
/// and to cover the three outcomes the planner can reach:
///
///   numeric-*  : short-circuited, one mode is provably optimal, planning never runs
///   alnum-120  : searched, split never wins
///   byte-150   : searched, split never wins, at the byte capacity boundary
///   alt1-120   : alternating every character, so switching never pays and the split loses
///   alt10-120  : alternating every ten, so the split wins big and the scan works hardest
///   mixed-*    : the common shape, split wins
///   url-38     : a realistic payload (R11x77 as one run, R15x43 split)
///   utf8-60    : multi-byte runs behind an ECI prefix
///
/// Every fixture fits under Single too, so no row is comparing a success with a throw.
/// ECC M only: the H capacities shift which version wins but not the shape of the work.
/// </summary>
public class RmQRSegmentationEncode
{
    private static readonly string[] shapeKeys =
    [
        "numeric-120", "numeric-361",
        "alnum-120", "byte-150",
        "mixed-20", "mixed-60", "mixed-120", "mixed-150",
        "alt1-120", "alt10-120",
        "url-38", "utf8-60",
    ];

    private Dictionary<string, string> _contents = default!;
    private string _content = default!;
    private byte[] _spanDestination = default!;

    [ParamsSource(nameof(Shapes))]
    public string Shape { get; set; } = default!;

    public static IEnumerable<string> Shapes() => shapeKeys;

    private static string HalfAndHalf(int total) => new string('a', total / 2) + new string('7', total - total / 2);

    [GlobalSetup]
    public void GlobalSetup()
    {
        _contents = new Dictionary<string, string>
        {
            ["numeric-120"] = new string('7', 120),
            ["numeric-361"] = new string('7', 361),                                                     // R17x139-M numeric boundary
            ["alnum-120"] = new string('A', 120),
            ["byte-150"] = new string('a', 150),                                                        // R17x139-M byte boundary
            ["mixed-20"] = HalfAndHalf(20),
            ["mixed-60"] = HalfAndHalf(60),
            ["mixed-120"] = HalfAndHalf(120),
            ["mixed-150"] = HalfAndHalf(150),
            ["alt1-120"] = string.Concat(Enumerable.Repeat("a7", 60)),                                  // alternating every character
            ["alt10-120"] = string.Concat(Enumerable.Repeat(new string('a', 10) + new string('7', 10), 6)),
            ["url-38"] = "https://example.com/p/1234567890123456",
            ["utf8-60"] = string.Concat(Enumerable.Repeat("日本7777", 10)),
        };

        _content = _contents[Shape];
        _spanDestination = new byte[RmQRCodeGenerator.GetRequiredBufferSize("0".AsSpan(), RmQREccLevel.M, RmQRVersion.R17x139).BufferSize];
    }

    [Benchmark(Baseline = true, Description = "Single")]
    public int SingleEncodeSpan()
    {
        return RmQRCodeGenerator.CreateRmQRCode(_content.AsSpan(), RmQREccLevel.M, _spanDestination);
    }

    [Benchmark(Description = "Optimal")]
    public int OptimalEncodeSpan()
    {
        return RmQRCodeGenerator.CreateRmQRCode(_content.AsSpan(), RmQREccLevel.M, _spanDestination, segmentation: RmQRSegmentation.Optimal);
    }
}

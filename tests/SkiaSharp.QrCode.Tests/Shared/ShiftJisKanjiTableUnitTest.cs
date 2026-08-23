using SkiaSharp.QrCode.Internals;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// The Kanji-mode Shift_JIS table, checked against JIS X 0208 itself rather than
/// against whatever produced it: the standard's published totals (6,879 cells =
/// 6,355 kanji + 524 non-kanji) and its row/cell layout are external invariants,
/// so a table generated from the wrong source cannot satisfy them by accident.
/// </summary>
public class ShiftJisKanjiTableUnitTest
{
    /// <summary>ISO/IEC 18004 8.4.5 compaction, computed independently of the production helper.</summary>
    private static int Index13(int sjis)
    {
        var shifted = sjis >= 0xE040 ? sjis - 0xC140 : sjis - 0x8140;
        return ((shifted >> 8) * 0xC0) + (shifted & 0xFF);
    }

    /// <summary>JIS X 0208 row/cell (ku-ten) to Shift_JIS, per the standard's conversion.</summary>
    private static int FromRowCell(int row, int cell)
    {
        var lead = row <= 62 ? 0x81 + (row - 1) / 2 : 0xC1 + (row - 1) / 2;
        int trail;
        if (row % 2 == 1)
        {
            trail = 0x3F + cell;
            if (trail >= 0x7F) trail++;
        }
        else
        {
            trail = 0x9E + cell;
        }
        return (lead << 8) | trail;
    }

    private static int CountAssignedInRows(int firstRow, int lastRow)
    {
        var total = 0;
        for (var row = firstRow; row <= lastRow; row++)
            for (var cell = 1; cell <= 94; cell++)
                if (ShiftJisKanjiTable.Lookup(Index13(FromRowCell(row, cell))) != '\0')
                    total++;
        return total;
    }

    /// <summary>The whole table holds exactly the JIS X 0208 repertoire.</summary>
    [Test]
    public async Task AssignedCells_MatchTheJisX0208Total()
    {
        var assigned = 0;
        for (var index = 0; index < ShiftJisKanjiTable.IndexCount; index++)
            if (ShiftJisKanjiTable.Lookup(index) != '\0')
                assigned++;

        await Assert.That(assigned).IsEqualTo(6879);
        await Assert.That(assigned).IsEqualTo(ShiftJisKanjiTable.AssignedCellCount)
            .Because("the declared count must track the data");
    }

    /// <summary>Non-kanji rows 1-8 hold 524 cells; kanji rows 16-84 hold 6,355.</summary>
    [Test]
    public async Task AssignedCells_SplitIntoTheStandardsNonKanjiAndKanjiTotals()
    {
        await Assert.That(CountAssignedInRows(1, 8)).IsEqualTo(524).Because("JIS X 0208 non-kanji repertoire");
        await Assert.That(CountAssignedInRows(16, 84)).IsEqualTo(6355).Because("JIS X 0208 level 1 + level 2 kanji");
    }

    /// <summary>Kanji rows are dense: 16-46 and 48-83 are full, 47 stops at cell 51, 84 at cell 6.</summary>
    [Test]
    [Arguments(16, 94)]
    [Arguments(46, 94)]
    [Arguments(47, 51)]
    [Arguments(48, 94)]
    [Arguments(83, 94)]
    [Arguments(84, 6)]
    public async Task KanjiRows_AreAssignedUpToTheirLastCell(int row, int lastAssignedCell)
    {
        for (var cell = 1; cell <= lastAssignedCell; cell++)
        {
            var mapped = ShiftJisKanjiTable.Lookup(Index13(FromRowCell(row, cell)));
            await Assert.That(mapped).IsNotEqualTo('\0').Because($"row {row} cell {cell} is assigned");
        }

        for (var cell = lastAssignedCell + 1; cell <= 94; cell++)
        {
            var mapped = ShiftJisKanjiTable.Lookup(Index13(FromRowCell(row, cell)));
            await Assert.That(mapped).IsEqualTo('\0').Because($"row {row} cell {cell} is past the end of the row");
        }
    }

    /// <summary>Rows 9-15 and 85-94 are unassigned in JIS X 0208, whatever CP932 puts there.</summary>
    [Test]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    [Arguments(12)]
    [Arguments(13)]
    [Arguments(14)]
    [Arguments(15)]
    [Arguments(85)]
    public async Task ReservedRows_AreUnassigned(int row)
    {
        for (var cell = 1; cell <= 94; cell++)
        {
            var sjis = FromRowCell(row, cell);
            if (sjis > 0xEBBF) continue; // past the Kanji-mode range entirely

            await Assert.That(ShiftJisKanjiTable.Lookup(Index13(sjis))).IsEqualTo('\0')
                .Because($"row {row} cell {cell} (0x{sjis:X4}) is not in JIS X 0208");
        }
    }

    /// <summary>
    /// Row 13 is the NEC extension block (circled digits, unit ligatures) that CP932
    /// assigns and JIS X 0208 does not. Choosing JIS X 0208 means these stay unmapped;
    /// this is the false-positive case a CP932-derived table would silently get wrong.
    /// </summary>
    [Test]
    [Arguments(0x8740)] // CP932 U+2460 circled digit one
    [Arguments(0x8741)] // CP932 U+2461
    [Arguments(0x875F)] // CP932 roman numeral
    [Arguments(0x879C)] // CP932 last NEC row 13 cell
    public async Task NecRow13_IsUnassigned(int sjis)
    {
        await Assert.That(ShiftJisKanjiTable.Lookup(Index13(sjis))).IsEqualTo('\0');
    }

    /// <summary>
    /// The seven cells where JIS X 0208 and CP932 disagree. These are the whole reason
    /// the mapping choice is a decision and not a detail: a CP932 table returns the
    /// right-hand column and mangles wave dashes, minus signs and currency symbols.
    /// </summary>
    [Test]
    [Arguments(0x815F, '\\', '＼')] // reverse solidus vs fullwidth reverse solidus
    [Arguments(0x8160, '〜', '～')] // wave dash vs fullwidth tilde
    [Arguments(0x8161, '‖', '∥')] // double vertical line vs parallel to
    [Arguments(0x817C, '−', '－')] // minus sign vs fullwidth hyphen-minus
    [Arguments(0x8191, '¢', '￠')] // cent sign vs fullwidth cent sign
    [Arguments(0x8192, '£', '￡')] // pound sign vs fullwidth pound sign
    [Arguments(0x81CA, '¬', '￢')] // not sign vs fullwidth not sign
    public async Task DivergentCells_UseTheJisX0208Reading(int sjis, char jisX0208, char cp932)
    {
        var mapped = ShiftJisKanjiTable.Lookup(Index13(sjis));
        await Assert.That(mapped).IsEqualTo(jisX0208);
        await Assert.That(mapped).IsNotEqualTo(cp932);
    }

    /// <summary>Spot checks across both Kanji-mode ranges and their boundaries.</summary>
    [Test]
    [Arguments(0x8140, '　')] // ideographic space, first cell of the range
    [Arguments(0x82B1, 'こ')] // hiragana ko
    [Arguments(0x889F, '亜')] // first level 1 kanji
    [Arguments(0x9FFC, '滌')] // last cell of the lower range
    [Arguments(0xE040, '漾')] // first cell of the upper range
    [Arguments(0xEAA4, '熙')] // last assigned JIS X 0208 cell
    public async Task KnownCells_MapToTheirJisX0208Character(int sjis, char expected)
    {
        await Assert.That(ShiftJisKanjiTable.Lookup(Index13(sjis))).IsEqualTo(expected);
    }

    /// <summary>0xEAA5 through 0xEBBF are inside the Kanji-mode range but past JIS X 0208.</summary>
    [Test]
    [Arguments(0xEAA5)]
    [Arguments(0xEB40)]
    [Arguments(0xEBBF)]
    public async Task CellsPastTheRepertoire_AreUnassigned(int sjis)
    {
        await Assert.That(ShiftJisKanjiTable.IsStructurallyValid(Index13(sjis))).IsTrue();
        await Assert.That(ShiftJisKanjiTable.Lookup(Index13(sjis))).IsEqualTo('\0');
    }

    /// <summary>
    /// Structural validity separates "corrupt bitstream" from "well-formed but not in
    /// the repertoire", which is what lets the decoder report InvalidBitstream and
    /// UnsupportedContent for the two different causes of a zero lookup.
    /// </summary>
    [Test]
    public async Task StructuralValidity_AcceptsExactlyTheExpressibleShiftJisPairs()
    {
        var valid = 0;
        for (var index = 0; index < ShiftJisKanjiTable.IndexCount; index++)
        {
            var low = index % 0xC0;
            var expected = low <= 0xBC && low != 0x3F;
            await Assert.That(ShiftJisKanjiTable.IsStructurallyValid(index)).IsEqualTo(expected)
                .Because($"index {index} (low byte 0x{low:X2})");
            if (expected) valid++;
        }

        // 42 full lead bytes × 188 expressible trails, plus lead 0xEB capped at 0xEBBF.
        await Assert.That(valid).IsEqualTo(8023);
    }

    /// <summary>Every assigned cell is structurally expressible; the reverse does not hold.</summary>
    [Test]
    public async Task EveryAssignedCell_IsStructurallyValid()
    {
        for (var index = 0; index < ShiftJisKanjiTable.IndexCount; index++)
        {
            if (ShiftJisKanjiTable.Lookup(index) == '\0') continue;
            await Assert.That(ShiftJisKanjiTable.IsStructurallyValid(index)).IsTrue().Because($"index {index}");
        }
    }
}

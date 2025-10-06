using System.Text;

namespace SkiaSharp.QrCode.Internals;

/// <summary>
/// Polynomial representation for Reed-Solomon calculations.
/// Collection of PolynomItem terms.
/// </summary>
internal class Polynom
{
    public List<PolynomItem> PolyItems { get; set; }

    public Polynom()
    {
        PolyItems = new List<PolynomItem>();
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        //this.PolyItems.ForEach(x => sb.Append("a^" + x.Coefficient + "*x^" + x.Exponent + " + "));
        foreach (var polyItem in this.PolyItems)
        {
            sb.Append("a^" + polyItem.Coefficient + "*x^" + polyItem.Exponent + " + ");
        }

        return sb.ToString().TrimEnd([' ', '+']);
    }
}

/// <summary>
/// Polynomial term with coefficient and exponent.
/// Used in Reed-Solomon error correction algorithm.
/// Can represent both alpha notation (α^n·x^m) and decimal notation.
/// </summary>
internal readonly record struct PolynomItem(int Coefficient, int Exponent);

namespace SkiaSharp.QrCode.Internals;

/// <summary>
/// Polynomial representation for Reed-Solomon calculations.
/// Collection of PolynomItem terms.
/// </summary>
internal class Polynom
{
    private readonly List<PolynomItem> _items = [];

    /// <summary>
    /// Gets the polynomial items
    /// </summary>
    public IReadOnlyList<PolynomItem> Items => _items;

    /// <summary>
    /// Add a polynomial item
    /// </summary>
    /// <param name="item"></param>
    public void Add(PolynomItem item) => _items.Add(item);

    /// <summary>
    /// Adds multiple polynomial items.
    /// </summary>
    public void AddRange(IEnumerable<PolynomItem> items) => _items.AddRange(items);

    /// <summary>
    /// Remove items matching the predicate.
    /// </summary>
    /// <param name="match"></param>
    public void RemoveAll(Predicate<PolynomItem> match) => _items.RemoveAll(match);

    /// <summary>
    /// Removes the item at the specified index.
    /// </summary>
    /// <param name="index"></param>
    public void RemoveAt(int index) => _items.RemoveAt(index);

    /// <summary>
    /// Sort PolynomItems by Exponent in descending order.
    /// </summary>
    public void SortByExponentDescending() => _items.Sort((a, b) => b.Exponent.CompareTo(a.Exponent));

    /// <summary>
    /// Gets the number of polynomial items.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Get the polynomial item at the specified index.
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    public PolynomItem this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }
}

/// <summary>
/// Polynomial term with coefficient and exponent.
/// Used in Reed-Solomon error correction algorithm.
/// Can represent both alpha notation (α^n·x^m) and decimal notation.
/// </summary>
internal readonly record struct PolynomItem(int Coefficient, int Exponent);

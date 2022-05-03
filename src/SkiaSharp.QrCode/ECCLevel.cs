namespace SkiaSharp.QrCode
{
    public enum ECCLevel
    {
        /// <summary>
        /// 7% may be lost before recovery is not possible
        /// </summary>
        L,
        /// <summary>
        /// 15% may be lost before recovery is not possible
        /// </summary>
        M,
        /// <summary>
        /// 25% may be lost before recovery is not possible
        /// </summary>
        Q,
        /// <summary>
        /// 30% may be lost before recovery is not possible
        /// </summary>
        H
    }
}

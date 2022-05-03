using System;
using System.Runtime.CompilerServices;

namespace SkiaSharp.QrCode.Image
{
    /// <summary>
    /// int version of Vector2, slim implementation
    /// </summary>
    /// <remarks>
    /// ref: https://github.com/dotnet/corefx/blob/master/src/System.Numerics.Vectors/src/System/Numerics/Vector2_Intrinsics.cs
    /// </remarks>
    public struct Vector2Slim
    {
        /// <summary>
        /// The X component of the vector.
        /// </summary>
        public int X;
        /// <summary>
        /// The Y component of the vector.
        /// </summary>
        public int Y;

        public Vector2Slim(int value) : this(value, value) { }
        public Vector2Slim(int x, int y) => (X, Y) = (x, y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(int[] array) => CopyTo(array, 0);
        public void CopyTo(int[] array, int index)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }
            if (index < 0 || index >= array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            if ((array.Length - index) < 2)
            {
                throw new ArgumentException($"{index} is greater thean destination.");
            }
            array[index] = X;
            array[index + 1] = Y;
        }

        public bool Equals(Vector2Slim other) => this.X == other.X && this.Y == other.Y;

        #region statics
        /// <summary>
        /// Returns the vector (0,0).
        /// </summary>
        public static Vector2Slim Zero => new Vector2Slim();

        /// <summary>
        /// Returns the vector (1,1).
        /// </summary>
        public static Vector2Slim One => new Vector2Slim(1, 1);

        /// <summary>
        /// Returns the vector (1,0).
        /// </summary>
        public static Vector2Slim UnitX => new Vector2Slim(1, 0);
        /// <summary>
        /// Returns the vector (0,1).
        /// </summary>
        public static Vector2Slim UnitY => new Vector2Slim(0, 1);
        #endregion
    }
}

// Minimal stand-ins for the ILGPU types used by the kernels, so that the kernel code compiles
// and runs sequentially on the CPU in this environment (ILGPU itself is not available here).
namespace ILGPU
{
    public readonly struct Index1D
    {
        public readonly int X;
        public Index1D(int x) { X = x; }
        public static implicit operator int(Index1D i) => i.X;
        public static implicit operator Index1D(int x) => new Index1D(x);
    }
}
namespace ILGPU.Runtime
{
    public readonly struct ArrayView<T> where T : unmanaged
    {
        private readonly T[] _a; private readonly long _off; public long Length { get; }
        public ArrayView(T[] a, long off = 0, long len = -1) { _a = a; _off = off; Length = len < 0 ? a.Length - off : len; }
        public ref T this[int i] => ref _a[_off + i];
        public ref T this[long i] => ref _a[_off + i];
        public ref T this[ILGPU.Index1D i] => ref _a[_off + i.X];
    }
}

// Entry point of the standalone build. When the .cs files of measurement/laptop are added to the benchmark project of
// the thesis instead, this folder is not used (see ../README.md).
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || !args[0].StartsWith("rev", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("usage: rev [BenchmarkDotNet options] | rev-kernels <data folder> [repetitions] [iGPU,dGPU] [inputs] | rev-env");
            return 1;
        }
        return RevEntry.Run(args);
    }
}

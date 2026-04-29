#nullable enable

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Plugins.Tests;

namespace Neo.Plugins.SignClient.Tests;

[TestClass]
public sealed class AssemblyHooks
{
    private static IDisposable? _engineProviderScope;

    [AssemblyInitialize]
    public static void Initialize(TestContext _)
    {
        _engineProviderScope = RiscvApplicationEngineTestProvider.Install();
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        TestBlockchain.DisposeSystem();
        _engineProviderScope?.Dispose();
    }
}

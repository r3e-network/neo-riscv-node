#nullable enable

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Plugins.Tests;

namespace Neo.Plugins.DBFTPlugin.Tests;

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
        MockBlockchain.DisposeSystem();
        _engineProviderScope?.Dispose();
    }
}

// Copyright (C) 2015-2026 The Neo Project.
//
// TestBlockchain.cs file belongs to the neo project and is free
// software distributed under the MIT software license, see the
// accompanying file LICENSE in the main directory of the
// repository or http://www.opensource.org/licenses/mit-license.php
// for more details.
//
// Redistribution and use in source and binary forms with or without
// modifications are permitted.

using Akka.Actor;
using Neo.Ledger;
using Neo.Persistence;
using Neo.Persistence.Providers;
using Neo.Plugins;
using System.Runtime.CompilerServices;

namespace Neo.Plugins.RpcServer.Tests;

public static class TestBlockchain
{
    public static readonly NeoSystem TheNeoSystem;
    public static readonly UInt160[] DefaultExtensibleWitnessWhiteList;
    private static readonly MemoryStore Store = new();
    private static readonly Plugin[] PreviousPlugins;

    internal class StoreProvider : IStoreProvider
    {
        public string Name => "TestProvider";

        public IStore GetStore(string path) => Store;
    }

    static TestBlockchain()
    {
        Console.WriteLine("initialize NeoSystem");
        RuntimeHelpers.RunClassConstructor(typeof(NeoSystem).TypeHandle);
        PreviousPlugins = Plugin.Plugins.ToArray();
        Plugin.Plugins.Clear();
        TheNeoSystem = new NeoSystem(TestProtocolSettings.Default, new StoreProvider());
    }

    internal static void ResetStore()
    {
        Store.Reset();
        TheNeoSystem.Blockchain.Ask(new Blockchain.Initialize()).ConfigureAwait(false).GetAwaiter().GetResult();
    }

    internal static DataCache GetTestSnapshot()
    {
        return TheNeoSystem.GetSnapshotCache().CloneCache();
    }

    internal static void DisposeSystem()
    {
        TheNeoSystem.Dispose();
        Store.Dispose();
        Plugin.Plugins.Clear();
        Plugin.Plugins.AddRange(PreviousPlugins);
    }
}

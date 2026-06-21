#nullable enable

using Neo.SmartContract;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Neo.Plugins.Tests;

internal static class RiscvApplicationEngineTestProvider
{
    private const string AdapterAssemblyName = "Neo.Riscv.Adapter";
    private const string AdapterEnvVar = "NEO_RISCV_ADAPTER_DLL";
    private const string HostLibEnvVar = "NEO_RISCV_HOST_LIB";
    private const string ProviderResolverTypeName = "Neo.SmartContract.RiscV.RiscvApplicationEngineProviderResolver";

    /// <summary>
    /// <see langword="true"/> when the last <see cref="Install"/> call could not load the
    /// RISC-V adapter and fell back to the managed NeoVM host provider. Tests that
    /// strictly require the RISC-V backend can read this to <c>Assert.Inconclusive</c>.
    /// </summary>
    public static bool LastInstallUsedNeoVmFallback { get; private set; }

    public static IDisposable Install()
    {
        var previous = ApplicationEngine.Provider;
        var previousHostLibraryPath = Environment.GetEnvironmentVariable(HostLibEnvVar);
        var preferred = TryCreateRiscvProvider(out var resolverType);
        if (preferred is null)
        {
            // The RISC-V adapter artifacts are optional for plugin tests: when they are
            // unavailable (e.g. neo-riscv-vm not built locally, or running on a CI image
            // without the native host lib), fall back to the pure-managed NeoVM host
            // provider so the broader plugin test suite still exercises the canonical
            // execution path instead of hard-failing the whole assembly. Callers that
            // strictly require the RISC-V backend can check LastInstallUsedNeoVmFallback.
            LastInstallUsedNeoVmFallback = true;
            ApplicationEngine.Provider = new NeoVMHostApplicationEngineProvider();
            return new RestoreScope(previous, resolverType: null, previousHostLibraryPath, usedNeoVmFallback: true);
        }

        LastInstallUsedNeoVmFallback = false;
        ApplicationEngine.Provider = preferred;
        return new RestoreScope(previous, resolverType, previousHostLibraryPath, usedNeoVmFallback: false);
    }

    private static IApplicationEngineProvider? TryCreateRiscvProvider(out Type? resolverType)
    {
        resolverType = null;
        var adapterAssemblyPath = ResolveAdapterAssemblyPath();
        if (adapterAssemblyPath is null)
            return null;
        var hostLibraryPath = ResolveHostLibraryPath();
        if (hostLibraryPath is null)
            return null;

        var previousHostLibraryPath = Environment.GetEnvironmentVariable(HostLibEnvVar);
        var changedHostLibraryPath = false;
        try
        {
            var assembly = LoadAdapterAssembly(adapterAssemblyPath);
            resolverType = assembly.GetType(ProviderResolverTypeName, throwOnError: true)!;
            var resolveMethod = resolverType.GetMethod(
                "ResolveRequiredProvider",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("ResolveRequiredProvider was not found.");

            Environment.SetEnvironmentVariable(HostLibEnvVar, hostLibraryPath);
            changedHostLibraryPath = true;
            return (IApplicationEngineProvider)(resolveMethod.Invoke(null, null)
                ?? throw new InvalidOperationException("ResolveRequiredProvider returned null."));
        }
        catch (Exception ex)
        {
            if (changedHostLibraryPath)
                Environment.SetEnvironmentVariable(HostLibEnvVar, previousHostLibraryPath);
            throw new InvalidOperationException(
                $"RISC-V adapter was found at '{adapterAssemblyPath}', but it could not be initialized.",
                ex);
        }
    }

    private static Assembly LoadAdapterAssembly(string adapterAssemblyPath)
    {
        var fullPath = Path.GetFullPath(adapterAssemblyPath);
        var loaded = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a =>
            string.Equals(a.GetName().Name, AdapterAssemblyName, StringComparison.Ordinal));
        if (loaded is not null)
        {
            if (string.Equals(Path.GetFullPath(loaded.Location), fullPath, StringComparison.OrdinalIgnoreCase))
                return loaded;

            throw new InvalidOperationException(
                $"A different {AdapterAssemblyName} assembly is already loaded from '{loaded.Location}', expected '{fullPath}'.");
        }

        return AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }

    private static string? ResolveAdapterAssemblyPath()
    {
        var configured = Environment.GetEnvironmentVariable(AdapterEnvVar);
        return FirstExistingFile(
            configured,
            TestBundlePath($"{AdapterAssemblyName}.dll"),
            SiblingVmPath(Path.Combine("dotnet", "Neo.Riscv.Adapter", "bin", "Debug", "net10.0", $"{AdapterAssemblyName}.dll")),
            SiblingVmPath(Path.Combine("dotnet", "Neo.Riscv.Adapter", "bin", "Release", "net10.0", $"{AdapterAssemblyName}.dll")));
    }

    private static string? ResolveHostLibraryPath()
    {
        var configured = Environment.GetEnvironmentVariable(HostLibEnvVar);
        return FirstExistingFile(
            configured,
            TestBundlePath(GetPlatformFileName()),
            SiblingVmPath(Path.Combine("target", "debug", GetPlatformFileName())),
            SiblingVmPath(Path.Combine("target", "release", GetPlatformFileName())));
    }

    private static string? TestBundlePath(string fileName)
    {
        var baseDirectory = AppContext.BaseDirectory;
        return FirstExistingFile(
            Path.Combine(baseDirectory, "RiscvAdapter", fileName),
            Path.Combine(Path.GetDirectoryName(baseDirectory) ?? baseDirectory, "RiscvAdapter", fileName));
    }

    private static string? SiblingVmPath(string relativePath)
    {
        foreach (var start in CandidateStartDirectories())
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "neo-riscv-vm", relativePath);
                if (File.Exists(candidate))
                    return candidate;
                directory = directory.Parent;
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateStartDirectories()
    {
        yield return AppContext.BaseDirectory;
        yield return Environment.CurrentDirectory;
    }

    private static string? FirstExistingFile(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    private static string GetPlatformFileName()
    {
        if (OperatingSystem.IsWindows())
            return "neo_riscv_host.dll";
        if (OperatingSystem.IsMacOS())
            return "libneo_riscv_host.dylib";
        return "libneo_riscv_host.so";
    }

    private static void ResetResolverForTesting(Type? resolverType)
    {
        resolverType?.GetMethod("ResetForTesting", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, null);
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly IApplicationEngineProvider? _previous;
        private readonly Type? _resolverType;
        private readonly string? _previousHostLibraryPath;

        public RestoreScope(
            IApplicationEngineProvider? previous,
            Type? resolverType,
            string? previousHostLibraryPath,
            bool usedNeoVmFallback = false)
        {
            _previous = previous;
            _resolverType = resolverType;
            _previousHostLibraryPath = previousHostLibraryPath;
        }

        public void Dispose()
        {
            ResetResolverForTesting(_resolverType);
            ApplicationEngine.Provider = _previous;
            Environment.SetEnvironmentVariable(HostLibEnvVar, _previousHostLibraryPath);
        }
    }
}

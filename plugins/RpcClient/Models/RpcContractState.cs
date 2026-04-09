// Copyright (C) 2015-2026 The Neo Project.
//
// RpcContractState.cs file belongs to the neo project and is free
// software distributed under the MIT software license, see the
// accompanying file LICENSE in the main directory of the
// repository or http://www.opensource.org/licenses/mit-license.php
// for more details.
//
// Redistribution and use in source and binary forms with or without
// modifications are permitted.

using Neo.Json;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;

namespace Neo.Network.RPC.Models;

public class RpcContractState
{
    public ContractState ContractState { get; set; }

    public JObject ToJson()
    {
        return ContractState.ToJson();
    }

    public static RpcContractState FromJson(JObject json)
    {
        var nef = RpcNefFile.FromJson((JObject)json["nef"]);
        var manifest = ContractManifest.FromJson((JObject)json["manifest"]);

        return new RpcContractState
        {
            ContractState = new ContractState
            {
                Id = (int)json["id"].AsNumber(),
                UpdateCounter = (ushort)json["updatecounter"].AsNumber(),
                Type = ResolveContractType(json, manifest, nef.Script),
                Hash = UInt160.Parse(json["hash"].AsString()),
                Nef = nef,
                Manifest = manifest
            }
        };
    }

    private static ContractType ResolveContractType(JObject json, ContractManifest manifest, ReadOnlyMemory<byte> script)
    {
        var typeToken = json["type"];
        if (typeToken is not null)
        {
            if (typeToken is JNumber)
                return (ContractType)(byte)typeToken.AsNumber();

            if (Enum.TryParse(typeToken.AsString(), ignoreCase: true, out ContractType parsedType))
                return parsedType;
        }

        var vmMarker = manifest.Extra?["vm"]?.AsString();
        if (!string.IsNullOrWhiteSpace(vmMarker))
        {
            return vmMarker switch
            {
                "legacy-neovm-v1" => ContractType.NeoVM,
                "riscv32-polkavm-v1" => ContractType.RiscV,
                _ => throw new FormatException($"Unsupported manifest extra.vm marker '{vmMarker}'.")
            };
        }

        return IsRiscvBinary(script) ? ContractType.RiscV : ContractType.NeoVM;
    }

    private static bool IsRiscvBinary(ReadOnlyMemory<byte> script)
    {
        var span = script.Span;
        return span.Length >= 4
            && span[0] == 0x50
            && span[1] == 0x56
            && span[2] == 0x4D
            && span[3] == 0x00;
    }
}

﻿using Konata.Core.Packets;
using Konata.Core.Attributes;
using Konata.Core.Common;
using Konata.Core.Events.Model;
using Konata.Core.Packets.Oicq;
using Konata.Core.Packets.Oicq.Model;
using Konata.Core.Packets.Tlv;
using Konata.Core.Packets.Tlv.Model;
using Konata.Core.Utils.Crypto;

// ReSharper disable RedundantAssignment
// ReSharper disable UnusedVariable
// ReSharper disable InvertIf
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable MemberCanBeMadeStatic.Global
// ReSharper disable SwitchStatementHandlesSomeKnownEnumValuesWithDefault
// ReSharper disable UnusedType.Global
// ReSharper disable MemberCanBeMadeStatic.Local

namespace Konata.Core.Components.Services.WtLogin;

[EventSubscribe(typeof(WtLoginEvent))]
[Service("wtlogin.exchange_emp", PacketType.TypeA, AuthFlag.WtLoginExchange, SequenceMode.Session)]
internal class ExchangeEmp : BaseService<WtLoginEvent>
{
    protected override bool Parse(SSOFrame input, AppInfo appInfo, 
        BotKeyStore keystore, out WtLoginEvent output)
    {
        // Parse oicq response
        var oicqResponse = new OicqResponse(input.Payload.GetBytes(), keystore.Ecdh);

        // Select status
        output = oicqResponse.Status switch
        {
            OicqStatus.OK => OnRecvXchgSuccess(oicqResponse, keystore),
            OicqStatus.PreventByTokenExpired => OnRecvTokenExpired(oicqResponse, keystore),
            _ => OnRecvUnknown(oicqResponse)
        };

        return true;
    }

    protected override bool Build(int sequence, WtLoginEvent input, AppInfo appInfo,
        BotKeyStore keystore, BotDevice device, ref PacketBase output)
    {
        output = null;
        // newSequence = sequence.GetSessionSequence("wtlogin.exchange_emp");

        // TODO:
        // Move this to logic layer

        // Update keys
        keystore.Session.TgtKey =
            new Md5Cryptor().Encrypt(keystore.Session.D2Key);

        // Build OicqRequest
        if (input.EventType == WtLoginEvent.Type.Xchg)
        {
            output = new OicqRequestXchg(sequence, appInfo, keystore, device);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Xchg success
    /// </summary>
    /// <param name="response"></param>
    /// <param name="keystore"></param>
    /// <returns></returns>
    private WtLoginEvent OnRecvXchgSuccess(OicqResponse response, BotKeyStore keystore)
    {
        var tlvs = response.BodyData.TakeAllBytes(out var _);
        var unpacker = new TlvUnpacker(tlvs, true);

        if (unpacker.Count == 1)
        {
            Tlv tlv119 = unpacker.TryGetTlv(0x119);
            if (tlv119 != null)
            {
                var decrypted = tlv119._tlvBody.TakeDecryptedBytes(out var _,
                    TeaCryptor.Instance, keystore.Session.TgtKey);

                var tlv119Unpacker = new TlvUnpacker(decrypted, true);
                Tlv tlv10a = tlv119Unpacker.TryGetTlv(0x10a); // tgt
                Tlv tlv10d = tlv119Unpacker.TryGetTlv(0x10d); // tgt key
                Tlv tlv11a = tlv119Unpacker.TryGetTlv(0x11a); // age, sex, nickname
                Tlv tlv305 = tlv119Unpacker.TryGetTlv(0x305); // d2key
                Tlv tlv143 = tlv119Unpacker.TryGetTlv(0x143); // d2
                // Tlv tlv120 = tlv119Unpacker.TryGetTlv(0x120); // skey
                // Tlv tlv114 = tlv119Unpacker.TryGetTlv(0x114); // st
                // Tlv tlv10e = tlv119Unpacker.TryGetTlv(0x10e); // st key
                // Tlv tlv103 = tlv119Unpacker.TryGetTlv(0x103); // stwx_web

                var tgtKey = ((T10dBody) tlv10d._tlvBody)._tgtKey;
                var tgtToken = ((T10aBody) tlv10a._tlvBody)._tgtToken;

                var d2Key = ((T305Body) tlv305._tlvBody)._d2Key;
                var d2Token = ((T143Body) tlv143._tlvBody)._d2Token;

                var userAge = ((T11aBody) tlv11a._tlvBody)._age;
                var userFace = ((T11aBody) tlv11a._tlvBody)._face;
                var userNickname = ((T11aBody) tlv11a._tlvBody)._nickName;

                // TODO: cleanup keys
                keystore.Session.TgtKey = tgtKey;
                keystore.Session.TgtToken = tgtToken;
                keystore.Session.D2Key = d2Key;
                keystore.Session.D2Token = d2Token;
                keystore.Account.Age = userAge;
                keystore.Account.Face = userFace;
                keystore.Account.Name = userNickname;
                keystore.Account.Age = userAge;

                return WtLoginEvent.ResultOk((int) response.Status);
            }
        }

        return OnRecvUnknown(response);
    }

    /// <summary>
    /// Token expired
    /// </summary>
    /// <param name="response"></param>
    /// <param name="keystore"></param>
    /// <returns></returns>
    private WtLoginEvent OnRecvTokenExpired(OicqResponse response, BotKeyStore keystore)
        => WtLoginEvent.ResultTokenExpired((int) response.Status);

    /// <summary>
    /// Unknown code
    /// </summary>
    /// <param name="response"></param>
    /// <returns></returns>
    private WtLoginEvent OnRecvUnknown(OicqResponse response)
        => WtLoginEvent.ResultUnknown((int) response.Status, "Unknown OicqRequest received.");
}

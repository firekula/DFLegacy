using DFLegacy.Protocol;
using Microsoft.Extensions.Logging;

namespace DFLegacy.Server;

/// <summary>Consumes client diagnostic reports without replies or gameplay state changes.</summary>
internal static class ClientDiagnosticHandler
{
    internal static bool TryHandle(PacketFrame request, ILogger logger, Guid? characterId, string? characterName)
    {
        if (request.Type != GameProtocolEngine.CommandPacketType)
            return false;

        if (request.ProtocolId == GameProtocolEngine.Act1TraceErrorCommand)
        {
            // ACT1 004AF42B / 004AF4A6 send type2/error through CMD125, corresponding to
            // official Dispatcher_TraceError::dispatch_sig@08204B0C. The u16 sequence is transport data.
            byte? type = request.Body.Length > sizeof(ushort) ? request.Body[sizeof(ushort)] : null;
            byte? error = type == 2 && request.Body.Length > sizeof(ushort) + 1
                ? request.Body[sizeof(ushort) + 1] : null;
            var status = type is null || (type == 2 && error is null)
                ? "Malformed" : type is 2 or 3 ? "Parsed" : "UnknownType";
            logger.LogWarning(
                "ACT1 client trace error: command={Command}, status={Status}, type={Type}, error={Error}, bodyLength={BodyLength}, character={CharacterId} ({Name}).",
                request.ProtocolId, status, type, error, request.Body.Length, characterId, characterName);
            return true;
        }

        if (request.ProtocolId == GameProtocolEngine.LegacyClientReportCommand)
        {
            // Preserve historical CMD118 code/flag samples; its several native packet builders
            // do not establish the official TRACE_ERROR semantics for this ACT1 command number.
            var parsed = EntranceService.TryReadTraceError(request.Body, out var code, out var flag);
            logger.LogWarning(
                "Historical client report: command={Command}, status={Status}, code={Code}, flag={Flag}, bodyLength={BodyLength}, character={CharacterId} ({Name}).",
                request.ProtocolId, parsed ? "Historical" : "Malformed",
                parsed ? (byte?)code : null, parsed ? (byte?)flag : null,
                request.Body.Length, characterId, characterName);
            return true;
        }

        return false;
    }
}

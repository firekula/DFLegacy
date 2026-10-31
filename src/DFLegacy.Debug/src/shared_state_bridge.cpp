#include "shared_state_bridge.h"
namespace dfl::client_state
{
    namespace { player_runtime::SharedState Shared{}; }
    bool BindShared(const player_runtime::SharedState* value) noexcept
    {
        if(!value||value->size!=sizeof(*value)||value->version!=player_runtime::SharedStateVersion
            ||value->snapshotSize!=sizeof(Snapshot)||value->readerSize!=sizeof(Reader)||!value->copyLatest||!value->frameTiming)return false;
        Shared=*value;return true;
    }
    Reader NativeReader() noexcept{return Shared.reader;}
    bool CopyLatest(Snapshot& value) noexcept{return Shared.copyLatest&&Shared.copyLatest(&value);}
    player_runtime::FrameTiming SharedTiming() noexcept { player_runtime::FrameTiming value{};if(Shared.frameTiming)Shared.frameTiming(&value);return value; }
    void Initialize(const wchar_t*,const wchar_t*,bool) noexcept{}
    void CaptureFrame() noexcept{}
    void ExportLatest() noexcept{}
    void Shutdown() noexcept{Shared={};}
}

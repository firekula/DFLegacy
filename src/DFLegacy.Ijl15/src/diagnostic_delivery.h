#pragma once
#include <cstdint>
#include <string>

namespace dfl::diagnostics
{
    class Delivery
    {
        std::string Published,Pending;
        std::uint64_t LastAttempt=0;
        bool Attempted=false;
    public:
        void Update(const char* record)
        {
            if(Published==record){Pending.clear();return;}
            if(Pending!=record)Pending=record;
        }
        template<class Writer> bool Flush(std::uint64_t now,Writer&& write)
        {
            if(Pending.empty()||(Attempted&&now-LastAttempt<1000))return false;
            Attempted=true;LastAttempt=now;
            if(!write(Pending))return false;
            Published.swap(Pending);Pending.clear();return true;
        }
    };
}

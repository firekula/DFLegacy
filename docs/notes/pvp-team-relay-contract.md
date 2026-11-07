# ACT1 PvP team and relay wire evidence

Authoritative client: DNF.exe MD5 `71f643d7b7339ca58d9bac6ded960c8f`, matching the running local fixtures. Evidence extracted read-only from the original IDB into the task workspace; original files were not altered.

- `0x86D280` mode buttons send CMD57 byte0/1/2 for individual/team/relay (3/4 are deathmatch and outside this change).
- `0x4FC510`, `0x86C080`: individual occupied seat0, teams1/2. Both teams nonempty and equal sized; host and all occupied participants ready. Seats254/255 are closed/open;3/4 observer roles are outside this change.
- `0x42360F` NOTI43: u16room, bytemode, bytecount, repeated byteseat/bytestate/u16uid. No later server GeoState byte.
- `0x423B27` NOTI45: bytemap/bytemode. `0x4246E2` NOTI46: bytedeadseat/bytekillerseat. `0x424765` NOTI47 legacy common result has winning representative seat (the client derives the team), per-recipient VP/rating, up to8 ranked rows, ace/all-kill UIDs, characterEXP and drawflag.
- **ACT1 relay is NOTI124/125**, not later server112/113. Native dispatch `0x42908C` reads count2; for each team it reads signed nextSeat (-1 means retain fighter) and byteorderCount followed by byte seat/u16 priority. `0x89C240` installs the fighter or retains the old fighter. Native `0x89DBF0`/`0x89DC20` handles transition animation. Native relay fight timer is120seconds (`0x89D0D0`); timeout kills the losing active fighter and reports normal death rather than ending the whole team match.
- `0x4291F0` NOTI125 reads one signed seat and toggles that player's request-to-fight marker. Native `0x89C2B0` sends empty CMD122 to request/cancel next participation.

A later original server with symbols supplies state-machine evidence after mapping mode1/2/3 to ACT1 mode0/1/2: `PvP_Room::set_pvp_mode` alternates teams when entering team mode from individual and preserves teams between team/relay. `check_team_pvp` checks equal counts. `check_winner` awards the entire winning team, including eliminated teammates. `CRelayBattleMgr` starts turns after7seconds; subsequent turns after3seconds, keeps an existing fighter, prioritizes requests then randomizes equal-priority players; each side has at most4 seats. All-kill requires >1 kills and at least the opposing start team size while still active.

Later `CalculateVictoryPointOfTeamPlay` gives losing participants1VP; winning team best scorer gets teamSize+2 and teammates teamSize. Existing proven ACT1 two-player individual4/1 behavior stays unchanged. Multi-individual ranking uses the original rank divisor progression. Rating uses the original pair expectations aggregated across opposing teams, retaining existing ACT1 novice protection and veteran floors. Later fair-PvP/deathmatch/mission/seal packet fields and channel-specific2014 rating modifiers are deliberately excluded.

Safety: match participant seat/team/grade are frozen at start; sender can only report own death, an enemy killer must be alive; a waiting relay member cannot report fighter death. Frozen result survives departures, missing reports and retry. Durable receipt covers mode, teams and seat identity; one atomic write preserves GM reward ledgers. Relay traffic remains within the same existing room/match UDP context.

Request-marker synchronization verified at native `0x89DBF0`, called by NOTI124 handler `0x4291C8`: it clears DWORD(this+292) and DWORD(this+296), all eight markers, before transition state6. Server clears every request at the same turn boundary.

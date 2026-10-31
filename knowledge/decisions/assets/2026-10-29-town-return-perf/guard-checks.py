import h,goaltest_v3 as g,json,time
# Existing native result must remain unchanged when the map is hidden.
r='A';before=g.route(r);h.keys(r,'A-map-hidden-guard',[dict(t=100,op='key',vk=78,dur=120),dict(t=500,op='move',x=410,y=286),dict(t=800,op='rclick',x=410,y=286,dur=120)],shot=True);after=g.route(r);assert before==after;assert not h.snapshot(r)['ui']['town_minimap']['visible'];h.save('A-hidden-no-route',dict(route_unchanged=True,snapshot=h.snapshot(r)))
# Visible NPC marker retains original UI behavior and must not create a native route.
r='B';before=g.route(r);h.keys(r,'B-NPC-marker-guard',[dict(t=100,op='move',x=239,y=268),dict(t=400,op='click',x=239,y=268,dur=120)],shot=True);after=g.route(r);assert before==after;h.save('B-NPC-no-route',dict(route_unchanged=True,snapshot=h.snapshot(r)))
print('PASS hidden map and NPC marker do not create routing requests')

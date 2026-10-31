from pathlib import Path
import json,hashlib,struct,math
import pefile,capstone
OUT=Path(__file__).resolve().parent
PE=Path(r"G:\Game\DNF_ACT1\DF\DNF.exe")
data=PE.read_bytes(); pe=pefile.PE(data=data); BASE=pe.OPTIONAL_HEADER.ImageBase
assert hashlib.sha256(data).hexdigest()=="6b4850beaf67ec61adb04ae17f119f16278dd05e97b8ffca871a9bdb547de0d5"
md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_32)
def rd(a,n):return pe.get_data(a-BASE,n)
def u32(a):return struct.unpack("<I",rd(a,4))[0]
ranges=[
("notification_dispatch_header",0x419420,0x41947B,"excerpt"),
("noti22_case",0x41E878,0x41E9C7,"complete switch case; enclosing function 419420"),
("remote_direction_decode",0x478F00,0x478F6E,"complete function"),
("remote_extrapolate_stop_deadband",0x4798A0,0x479A3E,"complete function"),
("set_town_destination",0x478F70,0x478FA5,"complete function"),
("town_steer",0x4795F0,0x479893,"complete function"),
("send_cmd37_if_position_changed",0x479A40,0x479B65,"complete function"),
("town_actor_update",0x47A8E0,0x47B5EA,"complete function"),
("map_actor_membership",0x4C5310,0x4C534D,"complete function"),
("user_to_town_actor",0x417AA0,0x417AA7,"complete function"),
("game_to_town_context",0x417DF0,0x417DF4,"complete function"),
("town_context_to_scene",0x40ABC0,0x40ABC4,"complete function"),
("scene_to_current_actor_collection",0x404380,0x404387,"complete function"),
("get_x",0x407460,0x40746B,"complete function"),
("get_y",0x407470,0x40747B,"complete function"),
("actor_get_integer_axis",0x471610,0x47161F,"complete function; tailcall 4C9BF0"),
("controller_get_integer_axis",0x4C9BF0,0x4C9C2D,"complete function"),
("float_to_nearest_integer",0x433940,0x433971,"complete function; tailcalls native ftol2 91B708"),
("protected_float_read",0x418260,0x4182CC,"complete function"),
("set_actor_integer_position",0x4715B0,0x4715EE,"complete function"),
("normal_actor_update_movement",0x473490,0x4735D9,"excerpt through Town virtual update invocation"),
]
manifest={"schema":"dfl-u07-native-town-position-contract/v1","source_pe":str(PE),"source_sha256":hashlib.sha256(data).hexdigest(),"image_base":hex(BASE),"capstone_version":capstone.__version__,"read_only_offline":True,"actual_live_branch_instrumented":False,"ranges":[]}
for name,a,b,kind in ranges:
 raw=rd(a,b-a);ins=list(md.disasm(raw,a));assert sum(i.size for i in ins)==len(raw),(name,hex(a))
 stem=f"{a:08X}-{name}"
 (OUT/(stem+".bin")).write_bytes(raw)
 (OUT/(stem+".asm")).write_text("\n".join(f"{i.address:08X} {i.bytes.hex():24} {i.mnemonic:8} {i.op_str}" for i in ins)+"\n",encoding="utf-8")
 manifest["ranges"].append({"name":name,"start":hex(a),"end_exclusive":hex(b),"size":b-a,"kind":kind,"sha256":hashlib.sha256(raw).hexdigest(),"raw":stem+".bin","asm":stem+".asm"})
# Bind the original jump table and concrete Town actor virtual slots to disk bytes.
assert u32(0x42A081+22*4)==0x41E878
vtbase=0x9988DC
slots={f"0x{x:x}":hex(u32(vtbase+x)) for x in [0x78,0x7C,0x80,0x84,0x88,0x94,0x98,0x154,0x170,0x174,0x178,0x1A4]}
(OUT/"town-vtable.bin").write_bytes(rd(vtbase,0x1A8))
(OUT/"notification-jumptable-21-24.bin").write_bytes(rd(0x42A081+21*4,16))
constants={hex(a):{"hex":rd(a,4).hex(),"float32":struct.unpack("<f",rd(a,4))[0]} for a in [0x99A770,0x99D6EC,0x99D6F0,0x99D6F4,0x99D6F8,0x9998B4,0xB0DF4C,0xA92C84]}
(OUT/"abi-and-constants.json").write_text(json.dumps({"vtable":hex(vtbase),"slots":slots,"noti22_jump_table_entry":{"address":hex(0x42A081+22*4),"value":hex(u32(0x42A081+22*4))},"float32_constants":constants},indent=2)+"\n",encoding="utf-8")
# Semantic model derived from instructions; it is NOT execution of the original game function.
def nround(x): return math.trunc(x+0.5 if x>=0 else x-0.5)
def decode(d):
 q,r=divmod(d-1,3);return [0,4,1][r], [3,4,2][q]
def stop_target(received,current):
 ci=nround(current);return ci if abs(received-ci)<50 else received
assert [decode(d) for d in range(1,10)]==[(0,3),(4,3),(1,3),(0,4),(4,4),(1,4),(0,2),(4,2),(1,2)]
assert stop_target(1049,1000.0)==1000
assert stop_target(1050,1000.0)==1050
assert stop_target(951,1000.0)==1000
assert stop_target(950,1000.0)==950
matrix=[]
N=OUT.parent
for f in sorted((N/"live").glob("*.json")):
 d=json.loads(f.read_text(encoding="utf-8-sig"));ss=d["samples"]
 first,last=ss[0],ss[-1]
 axes={}
 for axis in ["x","y"]:
  recv=nround(last["own"]["position"][axis]);before=first["peer"]["position"][axis];after=last["peer"]["position"][axis]
  axes[axis]={"own_final_float":last["own"]["position"][axis],"owner_virtual_integer_prediction":recv,"peer_before_float":before,"peer_before_virtual_integer":nround(before),"peer_final_float":after,"peer_final_destination":last["peer"]["destination"][axis],"stop_target_model_from_before_peer":stop_target(recv,before),"own_peer_final_delta":after-last["own"]["position"][axis],"model_equals_observed_destination":stop_target(recv,before)==last["peer"]["destination"][axis]}
 matrix.append({"file":f.name,"sha256":hashlib.sha256(f.read_bytes()).hexdigest(),"role":d["role"],"kind":d["kind"],"sample_count":len(ss),"start_t":first["t"],"last_t":last["t"],"peer_range_x":[min(s["peer"]["position"]["x"] for s in ss),max(s["peer"]["position"]["x"] for s in ss)],"peer_final_direction":last["peer"]["direction"],"axes":axes,"scope":"conditional model using first peer position and final owner-rounded position; no actual arrival-time branch capture; ground cases did not move owner"})
(OUT/"samples-and-semantic-model.json").write_text(json.dumps({"direction_decode_1_to_9":[decode(d) for d in range(1,10)],"threshold_boundary_checks_passed":4,"cases":matrix,"actual_original_function_execution":False,"not_a_netdump_analysis":True},indent=2)+"\n",encoding="utf-8")
manifest["live_input_hashes"]={x["file"]:x["sha256"] for x in matrix}
manifest["artifacts"]={f.name:hashlib.sha256(f.read_bytes()).hexdigest() for f in sorted(OUT.iterdir()) if f.is_file() and f.name!="manifest.json"}
(OUT/"manifest.json").write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf-8")
print(json.dumps({"source_sha256":manifest["source_sha256"],"range_count":len(ranges),"decode_boundary_checks":"PASS","case_count":len(matrix),"all_model_destinations_match":all(a["model_equals_observed_destination"] for r in matrix for a in r["axes"].values()),"manifest":str(OUT/"manifest.json")},indent=2))

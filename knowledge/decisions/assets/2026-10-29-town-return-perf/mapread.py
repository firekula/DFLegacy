import h,json,struct,sys
from ui import UI

def inspect(role):
 u=UI(role)
 try:
  s=h.snapshot(role);im=u.u(0xB5DCE8);m=u.u(im+0xff0);p=u.u(m+0xc);root=u.u(m+4)
  def regions(base):
   head=u.u(base+8);count=u.u(base+12);assert count<=128;node=u.u(head);out=[]
   while node!=head:
    assert len(out)<count
    record=u.u(node+12);town,area,rect=struct.unpack('<III',u.raw(record,12));r=struct.unpack('<iiii',u.raw(rect,16));out.append(dict(town=town,area=area,rect=r));node=u.u(node)
   assert len(out)==count;return out
  def control(p):
   try:return u.control(p)
   except ValueError:return dict(pointer=p,raw=u.raw(p,40).hex())
  out=dict(role=role,manager=im,minimap=m,plane=p,panel=control(root),shift=struct.unpack('<i',u.raw(0xBEEFC4,4))[0:1]+struct.unpack('<i',u.raw(0xB53C28,4)),plane_origin=struct.unpack('<ii',u.raw(p+4,8)),projects=regions(p+0x20),reals=regions(0xBEF024),menus={hex(o):control(u.u(m+o)) for o in [0x1c,0x64]},snapshot=s)
  try:out['controls']=u.tree(root)
  except ValueError:out['controls']=[]
  h.identity(role);assert u.u(im+0xff0)==m
  return out
 finally:u.close()
if __name__=='__main__':
 out=inspect(sys.argv[1]);h.save(sys.argv[2],out);out.pop('snapshot');print(json.dumps(out,ensure_ascii=True))

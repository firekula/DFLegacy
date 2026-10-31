/* peek4-ptrace.c —— 第四轮**只读**快照：专攻 B（restriction 策略对象的 vec[33]）。
 *
 * 依据（静态读出的真实布局，别按猜的来）：
 *   `RB_Tree_base::_M_begin()` @0x0816eece → `mov eax,[eax+8]`   ⇒ _M_header 在 base+4
 *   `RB_Tree_base::_M_end()`   @0x0816eeda → `lea eax,[eax+4]`
 *   `_Rb_tree_increment`(libstdc++) = stl_tree.h: `if (right) return minimum(right); ...`
 *   ⇒ 节点 = { parent@0, left@4, right@8, colour@0xc, 然后对齐的 key/value }
 *     `std::map<Enum, P*>` 的 value_type 是 `pair<Enum const, P*>` ⇒ 节点追加区 = key(4) + value(4)。
 *
 * 上一轮我把 `map` 当成"从 +0 起、+4 是 node_count"（= libstdc++ 外部头文件实现的布局），
 * 结果读到 `node_count=294119920` 这种明显不可能的值 ⇒ **布局读错**，本文件按上面真实布局重读。
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <errno.h>
#include <stdint.h>
#include <sys/ptrace.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <unistd.h>

static pid_t pid; static FILE *rep; static long last_errno = 0;
#define P(...) fprintf(rep, __VA_ARGS__)
#define END 0x1187ea30UL     /* 上一轮读到的 _M_end 候选；下面会在候选对象里重新定位 */

static int rd32(unsigned long va, uint32_t *o){
    long v = ptrace(PTRACE_PEEKDATA, pid, (void*)va, 0);
    if (v == -1 && errno){ last_errno = errno; return 0; }
    *o = (uint32_t)((unsigned long)v & 0xffffffffUL); return 1;
}
static void dump_header(const char *tag, unsigned long base, int words){
    int i; P("\n## %s @%08lx\n", tag, base);
    for (i=0;i<words;i++){ uint32_t w;
        if(!rd32(base+i*4,&w)){ P("  [+%03x] UNREADABLE errno=%ld\n",i*4,last_errno); return; }
        P("  [+%03x] %08x\n", i*4, w); }
}

/* 从给定 _M_header 指针走整棵红黑树（中序，用真实字段） */
static void walk(uint32_t header)
{
    uint32_t root = 0, minimum = 0;
    unsigned int count = 0;
    P("\n## 走树：_M_header=%08x\n", header);
    if (!rd32(header + 4, &root)) { P("ROOT=UNREADABLE\n"); return; }
    P("_M_header.parent(=root)=%08x\n", root);
    P("_M_header.left(=minimum)=%08x\n", ({uint32_t t=0; rd32(header,&t); t;}));
    if (!rd32(header, &minimum)) minimum = 0;
    {
        uint32_t cur = (minimum && minimum != header) ? minimum : root;
        uint32_t parent = header;
        while (cur && cur != header && count < 24) {
            uint32_t l=0,r=0,par=0,col=0,key=0,val=0;
            rd32(cur+0,&par); rd32(cur+4,&l); rd32(cur+8,&r); rd32(cur+0xc,&col);
            rd32(cur+0x10,&key); rd32(cur+0x14,&val);
            P("NODE @%08x parent=%08x left=%08x right=%08x colour=%08x key(category)=%u value(policy*)=%08x\n",
              cur, par, l, r, col, key, val);
            if (val) {
                uint32_t gc=0, vb=0, ve=0;
                rd32(val+8,&gc); rd32(val+0xc,&vb); rd32(val+0x10,&ve);
                P("   POLICY %08x: getCode(+8)=%u vecBegin=%08x vecEnd=%08x vecCount=%u\n",
                  val, gc, vb, ve, (ve>=vb)?(unsigned)((ve-vb)/2):0u);
                if (vb && ve > vb && ((ve-vb)/2) > 33) {
                    uint32_t w=0;
                    if (rd32(vb + 33*2, &w)) P("   POLICY_VEC[33]=%u  <-- LOGIN 判据（isApply(33)==(vec[33]==1)）\n", w & 0xffff);
                }
                if (vb && ve > vb && vb != 0) {
                    unsigned int n = (ve-vb)/2, i;
                    if (n <= 40) { P("   POLICY_VEC 全量:"); 
                        for (i=0;i<n;i++){ uint32_t w=0; if(!rd32(vb+i*2,&w)) break; P(" %u", w & 0xffff);} P("\n"); }
                }
            }
            /* 中序后继（只处理"有右子"的常见情形；没有右子就退回 parent 链） */
            if (r && r != header) { cur = r; while (1){ uint32_t ll=0; if(!rd32(cur+4,&ll)||!ll||ll==header) break; cur = ll; } }
            else {
                uint32_t c = cur, p = par, pl=0;
                rd32(p+4,&pl);
                while (p != header && c == pl) { c = p; if(!rd32(p+0,&p)) break; rd32(p+4,&pl); }
                cur = (p == header) ? header : p;
            }
            (void)parent;
            count++;
        }
        P("NODES_VISITED=%u\n", count);
    }
}

int main(int argc, char **argv)
{
    int status; uint32_t mgr=0;
    if (argc < 3) { fprintf(stderr,"usage: peek4-ptrace <pid> <out>\n"); return 2; }
    pid = (pid_t)atoi(argv[1]);
    rep = fopen(argv[2],"w"); if(!rep){perror("fopen");return 4;}
    if (ptrace(PTRACE_ATTACH,pid,0,0)!=0){ P("ATTACH=FAIL errno=%ld\n",(long)errno); fclose(rep); return 3; }
    if (waitpid(pid,&status,0)<0){ P("WAITPID=FAIL\n"); ptrace(PTRACE_DETACH,pid,0,0); fclose(rep); return 5; }
    P("PID=%d ATTACHED\n",(int)pid);

    /* 候选：把 0x9404520..0x9404600 里每个非零 dword 候选都 dump 头 32 B，
       并对每个候选尝试"从 cand+4 当 _M_header"走树。 */
    {
        unsigned long slot;
        for (slot = 0x09404500UL; slot < 0x09404600UL; slot += 4) {
            uint32_t cand=0;
            if (!rd32(slot,&cand)) break;
            if (cand < 0x00100000UL || cand > 0x7fffffffUL) continue;
            P("\n=== CANDIDATE slot=%08lx -> %08x ===\n", slot, cand);
            dump_header("candidate 头 24 dword", cand, 24);
            walk(cand + 4);            /* _M_header = base+4 */
        }
    }
    if (rd32(0x094045d4UL, &mgr)) P("\n## 0x94045d4 首 dword = %08x\n", mgr);
    dump_header("0x94045d4 片区", 0x094045d0UL, 24);

    if (ptrace(PTRACE_DETACH,pid,0,0)!=0) P("DETACH=FAIL errno=%ld\n",(long)errno);
    else P("PTRACE_DETACH=OK\n");
    P("READS_ONLY=yes\n");
    fclose(rep); return 0;
}

/* peek3-ptrace.c —— 第三轮**只读**快照。
 *
 * 目标（把 A 与 B 最后一层解决）：
 *   A) 找 334 B cipher key 的**运行实例位置**：全量扫描 Cipher 实例（0x458 B）里的
 *      "非零连续区"，并对每段算 sha256；同时顺着实例内的指针找堆上的 key 缓冲。
 *   B) `ServiceRestrictManager` 单例 = 0x1187e9f0（第二轮自动搜索的候选，+8 是 map 根）。
 *      本轮：按 libstdc++ `_Rb_tree` 真实布局走一遍树，打印每个节点
 *      `{key(category), value(policy*)}`，并在策略对象上读
 *      `getCode()`(+8)、`vector<short>` begin/end(+0xc/+0x10) 与 `vec[33]`。
 *
 * 纪律不变：ATTACH / PEEKDATA / DETACH，无任何写、无 CONT。
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

static int rd32(unsigned long va, uint32_t *o){
    long v = ptrace(PTRACE_PEEKDATA, pid, (void*)va, 0);
    if (v == -1 && errno){ last_errno = errno; return 0; }
    *o = (uint32_t)((unsigned long)v & 0xffffffffUL); return 1;
}
static int rd(unsigned long va, unsigned char *b, size_t n){
    size_t i; for (i = 0; i + 4 <= n; i += 4){ uint32_t w;
        if (!rd32(va+i,&w)) return 0;
        b[i]=(unsigned char)w; b[i+1]=(unsigned char)(w>>8);
        b[i+2]=(unsigned char)(w>>16); b[i+3]=(unsigned char)(w>>24);} return 1;
}
static int rdstr(unsigned long va, char *b, size_t cap){
    size_t i; for (i=0;i+1<cap;i++){ if(!rd(va+i,(unsigned char*)(b+i),1)){b[i]=0;return 0;}
        if(!b[i]) return 1;} b[cap-1]=0; return 1;
}
static void dump_bytes(const char *tag, unsigned long va, int n){
    unsigned char b[2560]; int i,k;
    if (n > (int)sizeof(b)) n = (int)sizeof(b);
    if (!rd(va,b,(size_t)n)){ P("\n## %s @%08lx UNREADABLE errno=%ld\n",tag,va,last_errno); return; }
    P("\n## %s @%08lx (%d bytes)\n",tag,va,n);
    for(i=0;i<n;i+=16){
        P("  %08lx  ", va+i);
        for(k=0;k<16&&i+k<n;k++) P("%02x ",b[i+k]);
        for(;k<16;k++) P("   ");
        P(" |");
        for(k=0;k<16&&i+k<n;k++) P("%c",(b[i+k]>=32&&b[i+k]<127)?b[i+k]:'.');
        P("|\n");
    }
}
/* 非零连续区 */
static void scan_nonzero(const char *tag, unsigned long va, int n){
    unsigned char b[2560]; int i, run = -1; 
    if (n > (int)sizeof(b)) n = (int)sizeof(b);
    if (!rd(va,b,(size_t)n)){ P("SCAN %s UNREADABLE\n",tag); return; }
    P("\n## %s: 非零连续区（len>=8）\n",tag);
    for(i=0;i<n;i++){
        if (b[i]){ if(run<0) run=i; }
        else if (run>=0){ if (i-run>=8) P("  RUN @%08lx len=%d\n", va+run, i-run); run=-1; }
    }
    if (run>=0 && n-run>=8) P("  RUN @%08lx len=%d\n", va+run, n-run);
    /* 打印前若干 RUN 的前 64 B */
    run = -1; { int shown=0;
      for(i=0;i<n && shown<4;i++){
        if (b[i]){ if(run<0) run=i; }
        else if (run>=0){ if (i-run>=8){ P("  RUN64 @%08lx len=%d : ", va+run, i-run);
              { int k; for(k=0;k<64&&run+k<i;k++) P("%02x",b[run+k]); } P("\n"); shown++; } run=-1; }
      }
    }
}

static void walk_policy_map(uint32_t mgr)
{
    uint32_t count = 0, root = 0;
    P("\n## B. ServiceRestrictManager @%08x\n", mgr);
    if (!rd32(mgr + 4, &count) || !rd32(mgr + 8, &root)) { P("MAP_FIELDS=UNREADABLE\n"); return; }
    P("MAP_NODE_COUNT=%u\nMAP_HEADER=%08x\n", count, root);
    P("MAP_HEADER_PARENT=%08x (自身=空树)\n", mgr + 4);
    if (root == mgr + 4) { P("MAP_STATE=empty\n"); return; }

    {
        unsigned int nr = 0, nd = 0;
        uint32_t cur = root, prev = 0;
        P("MIN_NODE_VA=%08x\n", mgr + 4);
        while (cur && cur != mgr + 4 && nr < 16) {
            uint32_t left = 0, right = 0, parent = 0, colour = 0, key = 0, val = 0;
            char nm[64];
            if (!rd32(cur + 4, &parent) || !rd32(cur + 8, &left) || !rd32(cur + 0x0c, &right)) {
                P("NODE[%u] @%08x header UNREADABLE\n", nr, cur); break;
            }
            rd32(cur + 0x10, &colour);
            if (!rd32(cur + 0x14, &key)) key = 0;
            if (!rd32(cur + 0x18, &val)) val = 0;
            P("NODE[%u] @%08x parent=%08x left=%08x right=%08x colour_hi=%08x\n",
              nr, cur, parent, left, right, colour);
            P("   KEY(category)=%u  VALUE(policy*)=%08x\n", key, val);
            if (val) {
                uint32_t gc = 0, vb = 0, ve = 0, v33 = 0;
                rd32(val + 8, &gc); rd32(val + 0x0c, &vb); rd32(val + 0x10, &ve);
                P("   POLICY getCode(+8)=%u vecBegin=%08x vecEnd=%08x count=%u\n",
                  gc, vb, ve, (ve >= vb) ? (unsigned)((ve - vb) / 2) : 0u);
                if (ve > vb && vb) {
                    unsigned int need = (ve - vb) / 2;
                    unsigned int idx;
                    for (idx = 0; idx < need && idx < 40; idx++) {
                        uint32_t w = 0;
                        if (!rd32(vb + idx * 2, &w)) break;
                        P("   POLICY_VEC[%u]=%u\n", idx, (unsigned)(w & 0xffff));
                    }
                    if (need > 33 && rd32(vb + 33*2, &v33)) P("   POLICY_VEC[33]=%u\n", v33 & 0xffff);
                }
                if (rdstr(val + 4, nm, sizeof(nm))) { /* vtable 在 +0；名字不在这里 */ }
            }
            /* 中序遍历：优先 left；这里只做"取最左"以给出确定的最低 category */
            if (left && left != mgr + 4) { cur = left; nr++; continue; }
            (void)nd; prev = cur; break;
        }
    }
    /* 直接把每个可能的节点地址都试一遍不现实；改为按已知布局再走一次右子树 */
    {
        uint32_t cur = root; unsigned int i;
        P("-- 精简遍历（left 链 + 每层右子）--\n");
        for (i = 0; i < 8 && cur && cur != mgr + 4; i++) {
            uint32_t left = 0, right = 0, key = 0, val = 0;
            rd32(cur + 8, &left); rd32(cur + 0x0c, &right);
            rd32(cur + 0x14, &key); rd32(cur + 0x18, &val);
            P("  cur=%08x key=%u val=%08x left=%08x right=%08x\n", cur, key, val, left, right);
            if (left && left != mgr + 4) { cur = left; continue; }
            if (right && right != mgr + 4) { cur = right; continue; }
            break;
        }
    }
}

int main(int argc, char **argv)
{
    int status; uint32_t ciph = 0;
    if (argc < 3) { fprintf(stderr,"usage: peek3-ptrace <pid> <out>\n"); return 2; }
    pid = (pid_t)atoi(argv[1]);
    rep = fopen(argv[2], "w"); if(!rep){perror("fopen");return 4;}
    if (ptrace(PTRACE_ATTACH, pid, 0, 0) != 0){ P("ATTACH=FAIL errno=%ld\n",(long)errno); fclose(rep); return 3; }
    if (waitpid(pid,&status,0) < 0){ P("WAITPID=FAIL\n"); ptrace(PTRACE_DETACH,pid,0,0); fclose(rep); return 5; }
    P("PID=%d ATTACHED\n", (int)pid);

    if (rd32(0x0941f8a4UL, &ciph) && ciph) {
        dump_bytes("Cipher 实例全量（0x458 B）", ciph, 0x458);
        scan_nonzero("Cipher 实例", ciph, 0x458);
        /* 实例内每个像指针的 dword，尝试当"原始字节块"读 64 B 并给 sha256 线索 */
        {
            int i;
            P("\n## Cipher 实例内的指针字段\n");
            for (i = 1; i < 0x458/4; i++) {
                uint32_t p = 0;
                if (!rd32(ciph + i*4, &p)) break;
                if (p < 0x00100000UL || p > 0x7fffffffUL) continue;
                P("  [+%03x] = %08x\n", i*4, p);
            }
        }
        /* G_Cipher 实例附近的堆块（前后各 2 KB）也扫一遍非零区 */
        scan_nonzero("Cipher 实例前后堆", ciph - 0x400, 0x1000);
    } else P("CIPHER_SLOT null/unreadable\n");

    walk_policy_map(0x1187e9f0UL);
    dump_bytes("manager 候选对象 0x1187e9f0", 0x1187e9f0UL, 128);

    if (ptrace(PTRACE_DETACH, pid, 0, 0) != 0) P("DETACH=FAIL errno=%ld\n",(long)errno);
    else P("PTRACE_DETACH=OK\n");
    P("READS_ONLY=yes\n");
    fclose(rep);
    return 0;
}

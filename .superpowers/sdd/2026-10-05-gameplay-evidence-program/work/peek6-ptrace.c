/* peek6-ptrace.c —— 第六轮**只读**快照：W1 定案（读 AuthMobileRestrictPolicy 的 vec[33]）。
 *
 * 静态依据（本轮新钉，两个独立来源）：
 *   ① `ServiceRestrictManager::isRestricted(Enum,int)` @0x0816e75e：
 *      `mov edx,[ebp+8]`（= this = ServiceRestrictManager*）→ 立刻 `mov [esp+4],edx` 交给
 *      `map<Enum,Policy*>::find` / `::end` ⇒ **map 成员就在 manager 偏移 0**。
 *   ② `RB_Tree_base::_M_begin()` @0x0816eece = `mov eax,[eax+8]` ⇒ `_M_header` 在 map+8，
 *      `_M_node_count` 在 map+4（libstdc++ `_Rb_tree_impl` 字段序：node_count 在前、header 在后）。
 *   ⇒ 运行期读：`mgr = 0x094045d4`；`count = [mgr+4]`；`header = mgr+8`；`root = [header+4]`；
 *     `node_key = [root+0x10]`（pair<Enum const, Policy*> 的 key）、`policy = [root+0x14]`。
 *
 * 纪律：**不做递归/图遍历**（上一轮就是这么把进程按住的）。所有取值都是有限次、带边界检查的
 * 直接 `PTRACE_PEEKDATA`，读不到就打印 UNREADABLE 并继续，最后必须 DETACH。
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

static int rd32(unsigned long va, uint32_t *o)
{
    long v;
    if (va < 0x1000UL) { last_errno = EFAULT; return 0; }   /* 边界守卫：绝不读空指针页 */
    v = ptrace(PTRACE_PEEKDATA, pid, (void *)va, 0);
    if (v == -1 && errno) { last_errno = errno; return 0; }
    *o = (uint32_t)((unsigned long)v & 0xffffffffUL);
    return 1;
}

/* 在一个节点地址上取 6 个字段；全成功才返回 1 */
static int node_fields(uint32_t n, uint32_t *par, uint32_t *l, uint32_t *r,
                       uint32_t *col, uint32_t *key, uint32_t *val)
{
    return rd32(n + 0, par) && rd32(n + 4, l) && rd32(n + 8, r) &&
           rd32(n + 0xc, col) && rd32(n + 0x10, key) && rd32(n + 0x14, val);
}

static void show_policy(uint32_t pol, const char *tag)
{
    uint32_t gc = 0, vb = 0, ve = 0, vt = 0;
    unsigned int cnt, i;
    P("\n-- %s policy=%08x\n", tag, pol);
    if (!pol) { P("   POLICY_NULL\n"); return; }
    if (rd32(pol, &vt)) P("   vtable(+0)=%08x\n", vt);
    if (!rd32(pol + 8, &gc)) { P("   getCode(+8)=UNREADABLE\n"); return; }
    P("   getCode(+8)=%u\n", gc);
    if (!rd32(pol + 0xc, &vb) || !rd32(pol + 0x10, &ve)) { P("   vec=UNREADABLE\n"); return; }
    P("   vecBegin(+0xc)=%08x vecEnd(+0x10)=%08x\n", vb, ve);
    if (ve < vb) { P("   vec INVALID (end<begin)\n"); return; }
    cnt = (ve - vb) / 2;
    P("   vec_count=%u\n", cnt);
    if (cnt > 64) { P("   (count>64，只打印前 40 项)\n"); cnt = 40; }
    for (i = 0; i < cnt; i++) {
        uint32_t w = 0;
        if (!rd32(vb + i * 2, &w)) { P("   vec[%u]=UNREADABLE errno=%ld\n", i, last_errno); break; }
        P("   vec[%u]=%u\n", i, (unsigned)(w & 0xffff));
    }
}

int main(int argc, char **argv)
{
    int status;
    uint32_t mgr = 0x094045d4UL, count = 0, header = 0, root = 0;
    uint32_t par = 0, l = 0, r = 0, col = 0, key = 0, val = 0;
    if (argc < 3) { fprintf(stderr, "usage: peek6-ptrace <pid> <outfile>\n"); return 2; }
    pid = (pid_t)atoi(argv[1]);
    rep = fopen(argv[2], "w");
    if (!rep) { perror("fopen"); return 4; }
    setvbuf(rep, NULL, _IONBF, 0);
    if (ptrace(PTRACE_ATTACH, pid, 0, 0) != 0) { P("ATTACH=FAIL errno=%ld\n", (long)errno); fclose(rep); return 3; }
    if (waitpid(pid, &status, 0) < 0) { P("WAITPID=FAIL\n"); ptrace(PTRACE_DETACH,pid,0,0); fclose(rep); return 5; }
    P("PID=%d ATTACHED\n", (int)pid);

    P("\n## manager 原始头（0x094045d4 起 12 dword）\n");
    {
        int i;
        for (i = 0; i < 12; i++) {
            uint32_t w = 0;
            if (!rd32(mgr + i * 4, &w)) { P("  [+%02x] UNREADABLE errno=%ld\n", i*4, last_errno); break; }
            P("  [+%02x] %08x\n", i * 4, w);
        }
    }

    header = mgr + 8;
    if (!rd32(mgr + 4, &count)) { P("COUNT=UNREADABLE\n"); goto out; }
    P("\nMANAGER_MAP_NODE_COUNT=%u\n", count);
    if (!rd32(header + 4, &root)) { P("HEADER_PARENT=UNREADABLE\n"); goto out; }
    P("MANAGER_MAP_HEADER=%08x\nMANAGER_MAP_ROOT=%08x\n", header, root);
    if (count == 0 || root == 0 || root == header) { P("MAP_STATE=empty（无策略）\n"); goto out; }

    if (!node_fields(root, &par, &l, &r, &col, &key, &val)) {
        P("ROOT_NODE=UNREADABLE errno=%ld\n", last_errno);
        goto out;
    }
    P("\n## root 节点 @%08x\n", root);
    P("  parent=%08x left=%08x right=%08x colour=%08x\n", par, l, r, col);
    P("  key(category)=%u\n  value(policy*)=%08x\n", key, val);
    show_policy(val, "root");

    /* 只再走一层（左/右各一个），不递归 */
    if (l && l != header) {
        if (node_fields(l, &par, &count, &r, &col, &key, &val)) {
            P("\n## left 子节点 @%08x key(category)=%u value=%08x\n", l, key, val);
            show_policy(val, "left-child");
        } else P("\nLEFT_CHILD=UNREADABLE\n");
    }
    if (r && r != header) {
        uint32_t a,b,c,d,e,f;
        if (node_fields(r, &a, &b, &c, &d, &e, &f)) {
            P("\n## right 子节点 @%08x key(category)=%u value=%08x\n", r, e, f);
            show_policy(f, "right-child");
        } else P("\nRIGHT_CHILD=UNREADABLE\n");
    }
    P("\n## 结论锚点（LOGIN 判据）：上面 root/子节点里 getCode==1 的那个策略的 vec[33]\n");

out:
    if (ptrace(PTRACE_DETACH, pid, 0, 0) != 0) P("DETACH=FAIL errno=%ld\n", (long)errno);
    else P("PTRACE_DETACH=OK\n");
    P("READS_ONLY=yes\n");
    fclose(rep);
    return 0;
}

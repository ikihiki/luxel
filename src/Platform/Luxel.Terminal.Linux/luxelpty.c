#define _GNU_SOURCE
#include <pty.h>
#include <stdlib.h>
#include <unistd.h>

/* Keep every child-side operation in native code. Returning to a managed runtime after fork is unsafe. */
int luxel_forkpty(const char *path, char *const argv[], char *const envp[], const char *cwd,
                  unsigned short rows, unsigned short columns, int *master)
{
    struct winsize size = { .ws_row = rows, .ws_col = columns, .ws_xpixel = 0, .ws_ypixel = 0 };
    pid_t pid = forkpty(master, NULL, NULL, &size);
    if (pid != 0)
        return pid;

    if (cwd != NULL && chdir(cwd) == -1)
        _exit(126);
    execve(path, argv, envp);
    _exit(127);
}

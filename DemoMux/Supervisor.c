#include <sys/event.h>
#include <sys/wait.h>
#include <sys/time.h>
#include <unistd.h>
#include <fcntl.h>
#include <signal.h>
#include <stdlib.h>
#include <errno.h>
#include <stdio.h>

// The app owns this tiny helper; it owns the component's process group.
// Kernel process-exit notifications still arrive if the app is killed/crashes.
static void shutdown_group(pid_t child) {
    kill(-child, SIGTERM);
    for (int attempt = 0; attempt < 12; attempt++) {
        if (waitpid(child, NULL, WNOHANG) == child) return;
        usleep(20000);
    }
    kill(-child, SIGKILL);
    while (waitpid(child, NULL, 0) < 0 && errno == EINTR) {}
}

int main(int argc, char **argv) {
    if (argc < 2 || argv[1][0] != '/') return 64;
    pid_t parent = getppid();
    if (parent <= 1) return 75;
    int queue = kqueue();
    if (queue < 0 || fcntl(queue, F_SETFD, FD_CLOEXEC) < 0) return 71;
    struct kevent changes[4];
    EV_SET(&changes[0], parent, EVFILT_PROC, EV_ADD | EV_ENABLE, NOTE_EXIT, 0, NULL);
    int signals[] = {SIGTERM, SIGINT, SIGHUP};
    for (int i = 0; i < 3; i++) {
        signal(signals[i], SIG_IGN);
        EV_SET(&changes[i + 1], signals[i], EVFILT_SIGNAL, EV_ADD | EV_ENABLE, 0, 0, NULL);
    }
    if (kevent(queue, changes, 4, NULL, 0, NULL) < 0 || getppid() != parent) return 75;
    pid_t child = fork();
    if (child < 0) return 71;
    if (child == 0) {
        if (setpgid(0, 0) < 0) _exit(71);
        for (int i = 0; i < 3; i++) signal(signals[i], SIG_DFL);
        signal(SIGPIPE, SIG_DFL);
        execv(argv[1], argv + 1);
        perror("DemoMux component");
        _exit(127);
    }
    setpgid(child, child);
    while (1) {
        int status = 0;
        pid_t finished = waitpid(child, &status, WNOHANG);
        if (finished == child) {
            kill(-child, SIGTERM);
            return WIFEXITED(status) ? WEXITSTATUS(status) : 128 + WTERMSIG(status);
        }
        if (finished < 0 && errno != EINTR) return 71;
        struct kevent event;
        struct timespec interval = {.tv_sec = 0, .tv_nsec = 100000000};
        int received = kevent(queue, NULL, 0, &event, 1, &interval);
        if ((received > 0 && (event.filter == EVFILT_SIGNAL ||
                             (event.filter == EVFILT_PROC && event.ident == (uintptr_t)parent))) ||
            getppid() != parent || (received < 0 && errno != EINTR)) {
            shutdown_group(child);
            return 0;
        }
    }
}

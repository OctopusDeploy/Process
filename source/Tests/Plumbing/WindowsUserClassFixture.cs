using System;

namespace Tests.Plumbing;

// Note: This leaves the user account lying around on your PC. We should probably delete it but it's the same account each time so not a big deal.
public class WindowsUserClassFixture
{
    const string Username = "test-shellexecutor";
    static readonly object Gate = new();

    public WindowsUserClassFixture()
    {
        // Multiple tests can use this fixture in parallel; the lock prevents a race condition
        // if they try to create the user account at the same time
        lock (Gate)
        {
            User = new TestUserPrincipal(Username);
        }
    }

    internal TestUserPrincipal User { get; }
}
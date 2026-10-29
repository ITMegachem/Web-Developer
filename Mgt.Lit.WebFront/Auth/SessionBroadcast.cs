namespace Mgt.Lit.WebFront.Auth;

// In-process "check your session NOW" signal between Blazor circuits on this WebFront server.
//
// A login / logout on this server raises it for that UserID; every open tab (circuit) of the same
// user runs its session check immediately instead of waiting for the next heartbeat:
//   - tabs in the SAME browser pick up the new refresh token from localStorage → refresh → keep going
//   - tabs on ANOTHER device can't refresh (old refresh tokens were revoked) → "Session Ended" at once
// Costs nothing while idle (no polling). Only reaches circuits on THIS server instance — the 15 s
// heartbeat in AppLayout still covers logins that happen elsewhere (another instance/app).
public sealed class SessionBroadcast
{
    public event Action<string>? CheckNow;   // arg = UserID

    public void Publish(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        var handlers = CheckNow;
        if (handlers == null) return;
        foreach (Action<string> h in handlers.GetInvocationList())
        {
            try { h(userId); } catch { /* one broken circuit must not stop the others */ }
        }
    }
}

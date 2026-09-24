namespace ExusiAI.Plugin.MishaShowcase;

internal sealed class CoalescingRefreshQueue
{
    private bool running;
    private bool requested;

    public bool Request()
    {
        requested = true;
        if (running) return false;
        running = true;
        return true;
    }

    public bool TakeNext()
    {
        if (requested)
        {
            requested = false;
            return true;
        }

        running = false;
        return false;
    }

    public void Reset()
    {
        running = false;
        requested = false;
    }
}

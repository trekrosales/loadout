using Loadout.Core;

namespace Loadout.Launching;

/// <summary>
/// Runs slow shell starts (ShellExecute, app activation) off the UI thread on one short-lived STA thread.
/// The thread exits as soon as its queue is empty, so nothing sits idle.
/// </summary>
internal static class StartWorker
{
    private static readonly Lock s_gate = new();
    private static readonly Queue<(string Name, Action Work)> s_queue = new();
    private static bool s_running;

    internal static void Enqueue(string name, Action work)
    {
        lock (s_gate)
        {
            s_queue.Enqueue((name, work));
            if (s_running)
            {
                return;
            }

            s_running = true;
        }

        try
        {
            var thread = new Thread(Run) { IsBackground = true, Name = "Loadout launcher" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        catch (Exception ex)
        {
            lock (s_gate)
            {
                s_running = false;
                s_queue.Clear();
            }

            Log.Error("Couldn't start the launcher thread.", ex);
        }
    }

    private static void Run()
    {
        while (true)
        {
            (string Name, Action Work) item;
            lock (s_gate)
            {
                if (!s_queue.TryDequeue(out item))
                {
                    s_running = false;
                    return;
                }
            }

            try
            {
                item.Work();
            }
            catch (Exception ex)
            {
                Log.Error("Starting failed: " + item.Name, ex);
            }
        }
    }
}

namespace VideoToMp3;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Single-instance guard: a second launch just exits quietly.
        using var mutex = new Mutex(initiallyOwned: true, "VideoToMp3_SingleInstance", out var isNew);
        if (!isNew)
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());

        GC.KeepAlive(mutex);
    }
}

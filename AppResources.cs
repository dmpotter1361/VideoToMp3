using System.Reflection;

namespace VideoToMp3;

/// <summary>Shared access to the app icon for windows and the tray.</summary>
public static class AppResources
{
    private static Icon? _icon;
    private static bool _tried;

    public static Icon? AppIcon
    {
        get
        {
            if (_tried) return _icon;
            _tried = true;

            // Prefer the embedded .ico (crisp at any size); fall back to the exe's icon.
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("appicon.ico");
                if (stream is not null)
                    _icon = new Icon(stream);
            }
            catch { /* fall through */ }

            if (_icon is null)
            {
                try
                {
                    var exe = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exe))
                        _icon = Icon.ExtractAssociatedIcon(exe);
                }
                catch { /* ignore */ }
            }

            return _icon;
        }
    }
}

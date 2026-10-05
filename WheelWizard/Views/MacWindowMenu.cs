using System.Runtime.InteropServices;

namespace WheelWizard.Views;

/// <summary>Registers an AppKit Window menu so macOS supplies its standard window actions and icons.</summary>
internal static class MacWindowMenu
{
    private const string ObjectiveC = "/usr/lib/libobjc.A.dylib";

    // Owned for the process lifetime. NSApplication and the active menu bar also retain this menu.
    private static nint _menu;

    public static void Attach()
    {
        if (!OperatingSystem.IsMacOS())
            return;
        var app = Send(GetClass("NSApplication"), Selector("sharedApplication"));
        var bar = Send(app, Selector("mainMenu"));
        var windowItem = Send(bar, Selector("itemWithTitle:"), String("Window"));
        if (windowItem == 0)
            return;

        if (_menu == 0)
        {
            _menu = Send(Send(GetClass("NSMenu"), Selector("alloc")), Selector("initWithTitle:"), String("Window"));
            // Standard responder-chain selectors give AppKit control over validation, icons and shortcuts.
            Send(
                _menu,
                Selector("addItemWithTitle:action:keyEquivalent:"),
                String("Minimize"),
                Selector("performMiniaturize:"),
                String("m")
            );
            Send(_menu, Selector("addItemWithTitle:action:keyEquivalent:"), String("Close"), Selector("performClose:"), String("w"));
        }
        if (Send(windowItem, Selector("submenu")) != _menu)
        {
            // Avalonia creates a separate menu bar for each window. AppKit permits a submenu
            // to have only one parent, so detach it before moving it into the active menu bar.
            var previousBar = Send(_menu, Selector("supermenu"));
            var previousCount = Send(previousBar, Selector("numberOfItems"));
            for (nint index = 0; index < previousCount; index++)
            {
                var previousItem = Send(previousBar, Selector("itemAtIndex:"), index);
                if (Send(previousItem, Selector("submenu")) == _menu)
                {
                    Send(previousItem, Selector("setSubmenu:"), 0);
                    break;
                }
            }
            Send(windowItem, Selector("setSubmenu:"), _menu);
        }
        if (Send(app, Selector("windowsMenu")) != _menu)
            Send(app, Selector("setWindowsMenu:"), _menu);
        // AppKit adds positioning/tiling items. Its native window constraints govern which are enabled.
        // Hide the public full-screen command; fixed-size windows cannot enter full screen anyway.
        var count = Send(_menu, Selector("numberOfItems"));
        for (nint index = 0; index < count; index++)
        {
            var item = Send(_menu, Selector("itemAtIndex:"), index);
            if (Send(item, Selector("action")) == Selector("toggleFullScreen:"))
                Send(item, Selector("setHidden:"), 1);
        }
    }

    private static nint String(string value) => SendString(GetClass("NSString"), Selector("stringWithUTF8String:"), value);

    [DllImport(ObjectiveC, EntryPoint = "objc_getClass")]
    private static extern nint GetClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveC, EntryPoint = "sel_registerName")]
    private static extern nint Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);

    [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint argument);

    [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint first, nint second, nint third);

    [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static extern nint SendString(nint receiver, nint selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string argument);
}

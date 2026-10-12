using System.Runtime.InteropServices;

namespace WheelWizard.Views.Shell;

/// <summary>Registers an AppKit Window menu so macOS supplies its standard window actions and icons.</summary>
internal static class MacWindowMenu
{
    private const string ObjectiveC = "/usr/lib/libobjc.A.dylib";

    // NSWindowCollectionBehavior flags from AppKit's public NSWindow API.
    private const nint FullScreenPrimary = 1 << 7;
    private const nint FullScreenAuxiliary = 1 << 8;
    private const nint FullScreenNone = 1 << 9;
    private const nint FullScreenAllowsTiling = 1 << 11;
    private const nint FullScreenDisallowsTiling = 1 << 12;

    // Owned for the process lifetime. NSApplication and the active menu bar also retain this menu.
    private static nint _menu;
    private static readonly InsertItemCallback InsertItem = InsertWindowItem;
    private static nint _insertItemImplementation;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void InsertItemCallback(nint menu, nint selector, nint item, nint index);

    private static void InsertWindowItem(nint menu, nint selector, nint item, nint index)
    {
        // AppKit creates these entries when the menu opens, after initial registration.
        // The unavailable full-screen tile group follows the positioning submenu
        // and has no action or submenu. Avoid matching localized system menu titles.
        if (Send(item, Selector("action")) == Selector("toggleFullScreen:"))
            Send(item, Selector("setHidden:"), 1);
        else if (
            Send(item, Selector("action")) == 0
            && Send(item, Selector("submenu")) == 0
            && Send(item, Selector("isSeparatorItem")) == 0
            && index > 0
            && Send(Send(menu, Selector("itemAtIndex:"), index - 1), Selector("submenu")) != 0
        )
            Send(item, Selector("setHidden:"), 1);
        Marshal.GetDelegateForFunctionPointer<InsertItemCallback>(_insertItemImplementation)(menu, selector, item, index);
    }

    private static nint CreateWindowMenuClass()
    {
        var menuClass = GetClass("NSMenu");
        var insertSelector = Selector("insertItem:atIndex:");
        _insertItemImplementation = GetMethodImplementation(menuClass, insertSelector);
        var customClass = AllocateClassPair(menuClass, "WheelWizardWindowMenu", 0);
        AddMethod(customClass, insertSelector, Marshal.GetFunctionPointerForDelegate(InsertItem), "v@:@q");
        RegisterClassPair(customClass);
        return customClass;
    }

    public static void Attach(nint window)
    {
        if (!OperatingSystem.IsMacOS())
            return;
        // Explicitly opt every window out of full screen, including split-screen tiling.
        var behavior = Send(window, Selector("collectionBehavior"));
        behavior &= ~(FullScreenPrimary | FullScreenAuxiliary | FullScreenAllowsTiling);
        behavior |= FullScreenNone | FullScreenDisallowsTiling;
        Send(window, Selector("setCollectionBehavior:"), behavior);
        var app = Send(GetClass("NSApplication"), Selector("sharedApplication"));
        var bar = Send(app, Selector("mainMenu"));
        var windowItem = Send(bar, Selector("itemWithTitle:"), String("Window"));
        if (windowItem == 0)
            return;

        if (_menu == 0)
        {
            _menu = Send(Send(CreateWindowMenuClass(), Selector("alloc")), Selector("initWithTitle:"), String("Window"));
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
        // Also cover items already present when this menu is reattached.
        var count = Send(_menu, Selector("numberOfItems"));
        for (nint index = 0; index < count; index++)
        {
            var item = Send(_menu, Selector("itemAtIndex:"), index);
            if (Send(item, Selector("action")) == Selector("toggleFullScreen:"))
                Send(item, Selector("setHidden:"), 1);
        }
    }

    [DllImport(ObjectiveC, EntryPoint = "objc_allocateClassPair")]
    private static extern nint AllocateClassPair(nint superclass, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nuint extraBytes);

    [DllImport(ObjectiveC, EntryPoint = "objc_registerClassPair")]
    private static extern void RegisterClassPair(nint cls);

    [DllImport(ObjectiveC, EntryPoint = "class_addMethod")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AddMethod(nint cls, nint selector, nint implementation, [MarshalAs(UnmanagedType.LPUTF8Str)] string types);

    [DllImport(ObjectiveC, EntryPoint = "class_getMethodImplementation")]
    private static extern nint GetMethodImplementation(nint cls, nint selector);

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

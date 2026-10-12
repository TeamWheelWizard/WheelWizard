using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Styling;
using Testably.Abstractions;
using WheelWizard.MiiAnimations.Library;
using WheelWizard.MiiImages;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Settings;
using WheelWizard.Shared.Calendar;

namespace WheelWizard.MiiImages.Views;

/// <summary>Composes rendering views inside standard Avalonia control templates.</summary>
public sealed class MiiControlThemes(
    IMiiImagesSingletonService images,
    ISeasonalCalendar calendar,
    IMiiNativeRenderer nativeRenderer,
    IMiiAnimationLibrary animations,
    IRandomSystem random,
    ISettingsManager settings
)
{
    public void Install(IResourceDictionary resources)
    {
        Add<MiiImageLoader>(resources, () => new MiiImageView(images, calendar));
        Add<MiiAnimatedImage>(
            resources,
            () => new MiiAnimatedView(images, calendar, nativeRenderer, animations, random.Random.Shared, settings)
        );
        Add<Mii3DRender>(resources, () => new MiiRenderView(images, calendar, nativeRenderer));
    }

    private static void Add<TControl>(IResourceDictionary resources, Func<BaseMiiImage> createView)
        where TControl : MiiImageControl =>
        resources[typeof(TControl)] = new ControlTheme(typeof(TControl))
        {
            Setters =
            {
                new Setter(
                    TemplatedControl.TemplateProperty,
                    new FuncControlTemplate<TControl>(
                        (control, scope) =>
                        {
                            var view = createView();
                            scope.Register("PART_Renderer", view);
                            PassInputs(control, view);
                            return view;
                        }
                    )
                ),
            },
        };

    /// <summary>
    /// The views take the controls' own properties (<c>AddOwner</c>), so every Mii property both have is bound
    /// straight through. Layout properties (Width, Margin, …) aren't: those stay on the control.
    /// </summary>
    private static void PassInputs(MiiImageControl control, BaseMiiImage view)
    {
        var registry = AvaloniaPropertyRegistry.Instance;
        foreach (var property in registry.GetRegistered(control))
        {
            if (typeof(MiiImageControl).IsAssignableFrom(property.OwnerType) && registry.IsRegistered(view, property))
                view.Bind(property, control.GetObservable(property));
        }
    }
}

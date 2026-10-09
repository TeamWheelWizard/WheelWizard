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
        resources[typeof(MiiImageLoader)] = new ControlTheme(typeof(MiiImageLoader))
        {
            Setters =
            {
                new Setter(
                    TemplatedControl.TemplateProperty,
                    new FuncControlTemplate<MiiImageLoader>(
                        (control, scope) =>
                        {
                            var view = new MiiImageView(images, calendar);
                            scope.Register("PART_Renderer", view);
                            view.Bind(BaseMiiImage.ReloadMethodProperty, control.GetObservable(MiiImageControl.ReloadMethodProperty));
                            view.Bind(
                                MiiImageView.LowQualitySpeedupProperty,
                                control.GetObservable(MiiImageLoader.LowQualitySpeedupProperty)
                            );
                            view.Bind(MiiImageView.LoadingColorProperty, control.GetObservable(MiiImageLoader.LoadingColorProperty));
                            view.Bind(MiiImageView.FallBackColorProperty, control.GetObservable(MiiImageLoader.FallBackColorProperty));
                            view.Bind(MiiImageView.ImageOnlyMarginProperty, control.GetObservable(MiiImageLoader.ImageOnlyMarginProperty));
                            view.Bind(MiiImageView.ImageVariantProperty, control.GetObservable(MiiImageLoader.ImageVariantProperty));
                            view.Bind(BaseMiiImage.MiiProperty, control.GetObservable(MiiImageControl.MiiProperty));
                            return view;
                        }
                    )
                ),
            },
        };
        resources[typeof(MiiImageLoaderWithHover)] = new ControlTheme(typeof(MiiImageLoaderWithHover))
        {
            Setters =
            {
                new Setter(
                    TemplatedControl.TemplateProperty,
                    new FuncControlTemplate<MiiImageLoaderWithHover>(
                        (control, scope) =>
                        {
                            var view = new MiiHoverView(images, calendar);
                            scope.Register("PART_Renderer", view);
                            view.Bind(BaseMiiImage.ReloadMethodProperty, control.GetObservable(MiiImageControl.ReloadMethodProperty));
                            view.Bind(MiiHoverView.IsHoveredProperty, control.GetObservable(MiiImageLoaderWithHover.IsHoveredProperty));
                            view.Bind(
                                MiiHoverView.LoadingColorProperty,
                                control.GetObservable(MiiImageLoaderWithHover.LoadingColorProperty)
                            );
                            view.Bind(
                                MiiHoverView.FallBackColorProperty,
                                control.GetObservable(MiiImageLoaderWithHover.FallBackColorProperty)
                            );
                            view.Bind(
                                MiiHoverView.ImageOnlyMarginProperty,
                                control.GetObservable(MiiImageLoaderWithHover.ImageOnlyMarginProperty)
                            );
                            view.Bind(
                                MiiHoverView.ImageVariantProperty,
                                control.GetObservable(MiiImageLoaderWithHover.ImageVariantProperty)
                            );
                            view.Bind(
                                MiiHoverView.HoverVariantProperty,
                                control.GetObservable(MiiImageLoaderWithHover.HoverVariantProperty)
                            );
                            view.Bind(BaseMiiImage.MiiProperty, control.GetObservable(MiiImageControl.MiiProperty));
                            return view;
                        }
                    )
                ),
            },
        };
        resources[typeof(MiiAnimatedImage)] = new ControlTheme(typeof(MiiAnimatedImage))
        {
            Setters =
            {
                new Setter(
                    TemplatedControl.TemplateProperty,
                    new FuncControlTemplate<MiiAnimatedImage>(
                        (control, scope) =>
                        {
                            var view = new MiiAnimatedView(images, calendar, nativeRenderer, animations, random.Random.Shared, settings);
                            scope.Register("PART_Renderer", view);
                            view.Bind(BaseMiiImage.ReloadMethodProperty, control.GetObservable(MiiImageControl.ReloadMethodProperty));
                            view.Bind(MiiAnimatedView.ImageVariantProperty, control.GetObservable(MiiAnimatedImage.ImageVariantProperty));
                            view.Bind(MiiAnimatedView.StillVariantProperty, control.GetObservable(MiiAnimatedImage.StillVariantProperty));
                            view.Bind(MiiAnimatedView.PerformanceProperty, control.GetObservable(MiiAnimatedImage.PerformanceProperty));
                            view.Bind(
                                MiiAnimatedView.MaxFramesPerSecondProperty,
                                control.GetObservable(MiiAnimatedImage.MaxFramesPerSecondProperty)
                            );
                            view.Bind(MiiAnimatedView.LoadingColorProperty, control.GetObservable(MiiAnimatedImage.LoadingColorProperty));
                            view.Bind(MiiAnimatedView.FallBackColorProperty, control.GetObservable(MiiAnimatedImage.FallBackColorProperty));
                            view.Bind(BaseMiiImage.MiiProperty, control.GetObservable(MiiImageControl.MiiProperty));
                            return view;
                        }
                    )
                ),
            },
        };
        resources[typeof(Mii3DRender)] = new ControlTheme(typeof(Mii3DRender))
        {
            Setters =
            {
                new Setter(
                    TemplatedControl.TemplateProperty,
                    new FuncControlTemplate<Mii3DRender>(
                        (control, scope) =>
                        {
                            var view = new MiiRenderView(images, calendar, nativeRenderer);
                            scope.Register("PART_Renderer", view);
                            view.Bind(BaseMiiImage.ReloadMethodProperty, control.GetObservable(MiiImageControl.ReloadMethodProperty));
                            view.Bind(MiiRenderView.ImageVariantProperty, control.GetObservable(Mii3DRender.ImageVariantProperty));
                            view.Bind(MiiRenderView.InteractiveProperty, control.GetObservable(Mii3DRender.InteractiveProperty));
                            view.Bind(BaseMiiImage.MiiProperty, control.GetObservable(MiiImageControl.MiiProperty));
                            return view;
                        }
                    )
                ),
            },
        };
    }
}

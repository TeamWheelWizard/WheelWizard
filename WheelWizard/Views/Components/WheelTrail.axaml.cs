using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace WheelWizard.Views.Components
{
    public class WheelTrail : TemplatedControl
    {
        public static readonly StyledProperty<double> AngleProperty = AvaloniaProperty.Register<WheelTrail, double>(nameof(Angle));

        public double Angle
        {
            get => GetValue(AngleProperty);
            set => SetValue(AngleProperty, value);
        }

        public static readonly StyledProperty<double> WheelRotationProperty = AvaloniaProperty.Register<WheelTrail, double>(
            nameof(WheelRotation)
        );

        public double WheelRotation
        {
            get => GetValue(WheelRotationProperty);
            set => SetValue(WheelRotationProperty, value);
        }

        public static readonly StyledProperty<double> RelativeLengthProperty = AvaloniaProperty.Register<WheelTrail, double>(
            nameof(RelativeLength),
            6.0
        );
        public double RelativeLength
        {
            get => GetValue(RelativeLengthProperty);
            set => SetValue(RelativeLengthProperty, value);
        }

        public static readonly StyledProperty<double> XProperty = AvaloniaProperty.Register<WheelTrail, double>(nameof(X));

        public double X
        {
            get => GetValue(XProperty);
            set => SetValue(XProperty, value);
        }

        public static readonly StyledProperty<double> YProperty = AvaloniaProperty.Register<WheelTrail, double>(nameof(Y));

        public double Y
        {
            get => GetValue(YProperty);
            set => SetValue(YProperty, value);
        }

        public static readonly StyledProperty<double> ExtendedHeightProperty = AvaloniaProperty.Register<WheelTrail, double>(
            nameof(ExtendedHeight)
        );

        public double ExtendedHeight
        {
            get => GetValue(ExtendedHeightProperty);
            set => SetValue(ExtendedHeightProperty, value);
        }

        private readonly RotateTransform _rotateTransform = new RotateTransform { CenterX = 0.5, CenterY = 0.5 };
        private readonly TranslateTransform _translateTransform = new TranslateTransform { X = 0, Y = 0 };

        public WheelTrail()
        {
            var group = new TransformGroup();
            group.Children.Add(_rotateTransform);
            group.Children.Add(_translateTransform);
            this.RenderTransform = group;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == AngleProperty)
            {
                _rotateTransform.Angle = (double)(change.NewValue ?? 0.0);
            }

            if (change.Property == XProperty)
            {
                _translateTransform.X = (double)(change.NewValue ?? 0.0);
            }
            if (change.Property == YProperty)
            {
                _translateTransform.Y = (double)(change.NewValue ?? 0.0);
            }
        }
    }
}

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OpenClicker
{
    public partial class OverlayWindow : Window
    {
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int GWL_EXSTYLE = -20;

        private bool isRunning = false;

        public OverlayWindow()
        {
            InitializeComponent();

            Loaded += OverlayWindow_Loaded;
        }

        private void OverlayWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            MakeClickThrough();
            PositionOverlay();

            if (isRunning)
                StartAnimation();
        }

        public void SetToggleKey(string keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName))
                keyName = "F6";

            RunOnUiThread(() =>
            {
                StopKeyText.Text =
                    $"Press {keyName} to stop";
            });
        }

        public void SetStats(
            double projectedCps,
            long lifetimeClicks)
        {
            RunOnUiThread(() =>
            {
                CpsText.Text =
                    FormatCps(projectedCps);

                LifetimeText.Text =
                    FormatCount(lifetimeClicks);
            });
        }

        private void RunOnUiThread(Action action)
        {
            if (Dispatcher.CheckAccess())
            {
                action();
                return;
            }

            Dispatcher.BeginInvoke(action);
        }

        private string FormatCps(double cps)
        {
            if (cps <= 0)
                return "0.0";

            if (cps >= 1000)
                return cps.ToString("0");

            return cps.ToString("0.0");
        }

        private string FormatCount(long count)
        {
            if (count < 1_000)
                return count.ToString();

            if (count < 1_000_000)
                return $"{count / 1_000.0:0.#}K";

            if (count < 1_000_000_000)
                return $"{count / 1_000_000.0:0.#}M";

            return $"{count / 1_000_000_000.0:0.##}B";
        }

        private void MakeClickThrough()
        {
            IntPtr hwnd =
                new WindowInteropHelper(this).Handle;

            int extendedStyle =
                GetWindowLong(
                    hwnd,
                    GWL_EXSTYLE);

            SetWindowLong(
                hwnd,
                GWL_EXSTYLE,
                extendedStyle |
                WS_EX_TRANSPARENT |
                WS_EX_NOACTIVATE);
        }

        private void PositionOverlay()
        {
            double workAreaRight =
                SystemParameters.WorkArea.Right;

            double workAreaBottom =
                SystemParameters.WorkArea.Bottom;

            Left =
                workAreaRight -
                Width -
                25;

            Top =
                workAreaBottom -
                Height -
                25;
        }

        public void SetRunning(bool running)
        {
            RunOnUiThread(() =>
            {
                isRunning = running;

                if (running)
                {
                    Visibility =
                        Visibility.Visible;

                    PositionOverlay();
                    StartAnimation();
                }
                else
                {
                    StopAnimation();

                    Visibility =
                        Visibility.Hidden;
                }
            });
        }

        private void StartAnimation()
        {
            OverlayTransform1.BeginAnimation(
                TranslateTransform.YProperty,
                null);

            OverlayTransform2.BeginAnimation(
                TranslateTransform.YProperty,
                null);

            OverlayTransform1.Y = 0;
            OverlayTransform2.Y = 0;

            DoubleAnimation animation1 =
                new DoubleAnimation
                {
                    From = 0,
                    To = -5,
                    Duration =
                        TimeSpan.FromMilliseconds(350),

                    AutoReverse = true,

                    RepeatBehavior =
                        RepeatBehavior.Forever
                };

            DoubleAnimation animation2 =
                new DoubleAnimation
                {
                    From = 0,
                    To = -5,
                    Duration =
                        TimeSpan.FromMilliseconds(350),

                    AutoReverse = true,

                    BeginTime =
                        TimeSpan.FromMilliseconds(170),

                    RepeatBehavior =
                        RepeatBehavior.Forever
                };

            OverlayTransform1.BeginAnimation(
                TranslateTransform.YProperty,
                animation1);

            OverlayTransform2.BeginAnimation(
                TranslateTransform.YProperty,
                animation2);
        }

        private void StopAnimation()
        {
            OverlayTransform1.BeginAnimation(
                TranslateTransform.YProperty,
                null);

            OverlayTransform2.BeginAnimation(
                TranslateTransform.YProperty,
                null);

            OverlayTransform1.Y = 0;
            OverlayTransform2.Y = 0;
        }

        public void RefreshTheme()
        {
            RunOnUiThread(() =>
            {
                InvalidateVisual();
                UpdateLayout();
            });
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(
            IntPtr hWnd,
            int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(
            IntPtr hWnd,
            int nIndex,
            int dwNewLong);
    }
}
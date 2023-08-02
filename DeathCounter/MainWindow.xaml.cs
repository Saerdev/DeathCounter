using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Forms;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Drawing.Imaging;
using Tesseract;
using Color = System.Drawing.Color;

namespace DeathCounter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // DLL libraries used to manage hotkeys
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vlc);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        //https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-hotkey
        const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll", SetLastError = true)]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        private const int GWL_EX_STYLE = -20;
        private const int WS_EX_APPWINDOW = 0x00040000, WS_EX_TOOLWINDOW = 0x00000080;

        const int SCREENSHOT_TIMER_MS = 600;
        int deathCounter = 0;

        [Flags]
        public enum Modifiers
        {
            NoMod = 0x0000,
            Alt = 0x0001,
            Ctrl = 0x0002,
            Shift = 0x0004,
            Win = 0x0008
        }

        enum Hotkeys {
            Increment,
            Decrement,
            Reset,
            Test,
            Quit
        }

        private NotifyIcon notifyIcon = new NotifyIcon();
        StringBuilder trayText = new StringBuilder();

        const int DEATH_INCREMENT_COOLDOWN = 5;
        int deathIncrementCooldownTimer = 0;
        bool isDraggingWindow = false;

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();
            SetupTrayIcon();
            StartOCRLoop();
        }

        void StartOCRLoop()
        {
            System.Threading.Timer screenshotTimer = new System.Threading.Timer(new System.Threading.TimerCallback(OCRLoop));
            screenshotTimer.Change(0, SCREENSHOT_TIMER_MS);
        }

        void OCRLoop(object? obj)
        {
            TakeScreenshot(null);
        }

        void TakeScreenshot(object? obj)
        {
            if (deathIncrementCooldownTimer > 0 || isDraggingWindow)
            {
                deathIncrementCooldownTimer--;
                return;
            }

            double halfScreenWidth = SystemParameters.PrimaryScreenWidth / 2;
            double halfScreenHeight = SystemParameters.PrimaryScreenHeight / 2;

            using (Bitmap bmp = new Bitmap((int)halfScreenWidth, (int)halfScreenHeight))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    //Opacity = .0;
                    g.CopyFromScreen((int)SystemParameters.PrimaryScreenWidth / 3, 600, 400, 500, bmp.Size);
                    //Opacity = 1;

                    using (MemoryStream memoryStream = new MemoryStream())
                    {
                        // Invert bitmap before sending to OCR for better clarity
                        InvertBitmap(bmp).Save(memoryStream, System.Drawing.Imaging.ImageFormat.Bmp);
                        memoryStream.Seek(0, System.IO.SeekOrigin.Begin);
                        ProcessOCR(memoryStream);
                    }
                }
            }
         }

        Bitmap InvertBitmap(Bitmap bmp)
        {
            LockBitmap lockBitmap = new LockBitmap(bmp);
            lockBitmap.LockBits();
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    // get pixel value
                    Color p = lockBitmap.GetPixel(x, y);

                    // extract ARGB value from p
                    int a = p.A;
                    int r = p.R;
                    int g = p.G;
                    int b = p.B;

                    // find negative value
                    r = 255 - 4;
                    g = 255 - g;
                    b = 255 - b;

                    // set new ARGB value in pixel
                    lockBitmap.SetPixel(x, y, Color.FromArgb(a, r, g, b));
                }
            }
            lockBitmap.UnlockBits();
            return bmp;
        }

        void ProcessOCR(Stream imageStream)
        {
            using (var engine = new TesseractEngine(@".\tessdata", "eng", EngineMode.Default))
            {
                using (var image = new System.Drawing.Bitmap(imageStream))
                {
                    using (var pix = PixConverter.ToPix(image))
                    {
                        using (var page = engine.Process(pix))
                        {
                            Trace.WriteLine(page.GetText());

                            if (page.GetText().Contains("YOU ARE DEAD"))
                            {
                                ModifyCounter((int)Hotkeys.Increment);
                                deathIncrementCooldownTimer = DEATH_INCREMENT_COOLDOWN;
                            }
                        }
                    }
                }
            }
        }

        private void SetupTrayIcon()
        {
            trayText.AppendLine("    Death Counter");
            trayText.AppendLine("Increment: Ctrl + Enter ");
            trayText.AppendLine("Decrement: Ctrl + Subtract");
            trayText.AppendLine("Reset: Ctrl + Add ");
            trayText.Append("Quit: Ctrl + Numpad0");

            notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
            notifyIcon.Text = trayText.ToString();
            notifyIcon.MouseClick += Handle_TooltipMouseClick;
            notifyIcon.Visible = true;

            ShowInTaskbar = false;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RemoveFromAltTab();
            SetupHotkeys();
        }

        private void RemoveFromAltTab()
        {
            //Variable to hold the handle for the form
            var helper = new WindowInteropHelper(this).Handle;
            //Performing some magic to hide the form from Alt+Tab
            SetWindowLong(helper, GWL_EX_STYLE, (GetWindowLong(helper, GWL_EX_STYLE) | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
        }

        private void SetupHotkeys()
        {
            var source = PresentationSource.FromVisual(this as Visual) as HwndSource;
            if (source == null)
                throw new Exception("Could not create hWnd source from window.");
            source.AddHook(ProcessHotkeys);

            RegisterHotKey(new WindowInteropHelper(this).Handle, (int)Hotkeys.Increment, (int)Modifiers.Ctrl, (int)Keys.Enter);
            RegisterHotKey(new WindowInteropHelper(this).Handle, (int)Hotkeys.Decrement, (int)Modifiers.Ctrl, (int)Keys.Add);
            RegisterHotKey(new WindowInteropHelper(this).Handle, (int)Hotkeys.Reset, (int)Modifiers.Ctrl, (int)Keys.Subtract);
            RegisterHotKey(new WindowInteropHelper(this).Handle, (int)Hotkeys.Quit, (int)Modifiers.Ctrl, (int)Keys.NumPad0);
            RegisterHotKey(new WindowInteropHelper(this).Handle, (int)Hotkeys.Test, (int)Modifiers.Ctrl, (int)Keys.NumPad1);
        }


        private IntPtr ProcessHotkeys(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                ModifyCounter(wParam.ToInt32());
            }

            return IntPtr.Zero;
        }

        void ModifyCounter(int modification)
        {
            switch (modification)
            {
                case (int)Hotkeys.Increment:
                    deathCounter++;
                    break;
                case (int)Hotkeys.Decrement:
                    deathCounter--;
                    break;
                case (int)Hotkeys.Reset:
                    deathCounter = 0;
                    break;
                case (int)Hotkeys.Quit:
                    Close();
                    break;
                case (int)Hotkeys.Test:
                    TakeScreenshot(null);
                    break;
                default:
                    break;
            }

            UpdateCounter();
        }

        void UpdateCounter()
        {
            if (System.Windows.Application.Current.Dispatcher.CheckAccess())
                Death_Counter.Content = "Deaths: " + deathCounter;
            else
                System.Windows.Application.Current.Dispatcher.Invoke(() => UpdateCounter());
        }

        private void Handle_TooltipMouseClick(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Activate();
            }
            else if (e.Button == MouseButtons.Right)
            {
                Close();
            }
        }

        private void Rectangle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Make window draggable
            if (e.ChangedButton == MouseButton.Left)
            {
                isDraggingWindow = true;
                Main_Window.DragMove();
            }
        }

        private void Rectangle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                isDraggingWindow = false;
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            SaveSettings();
            notifyIcon.Dispose();
        }

        private void SaveSettings()
        {
            Properties.Settings.Default.MainWindowLeft = Left;
            Properties.Settings.Default.MainWindowTop = Top;
            Properties.Settings.Default.Deaths = deathCounter;
            Properties.Settings.Default.Save();
        }

        private void LoadSettings()
        {
            this.Left = Properties.Settings.Default.MainWindowLeft == 0 ? SystemParameters.PrimaryScreenWidth / 2 : Properties.Settings.Default.MainWindowLeft;
            this.Top = Properties.Settings.Default.MainWindowTop == 0 ? SystemParameters.FullPrimaryScreenHeight / 2 : Properties.Settings.Default.MainWindowTop;
            deathCounter = Properties.Settings.Default.Deaths;
            Death_Counter.Content = "Deaths: " + deathCounter;
        }

        private void Rectangle_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            isDraggingWindow = false;
        }
    }
}

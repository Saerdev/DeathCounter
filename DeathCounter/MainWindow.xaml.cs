#define LIES_OF_P

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
using Color = System.Drawing.Color;
using System.Collections.Generic;
using static System.Net.WebRequestMethods;
using System.Windows.Forms.VisualStyles;
using System.Linq;
using static DeathCounter.Utilities;
using System.Threading.Tasks;

#if !LIES_OF_P
using Tesseract;
#endif

namespace DeathCounter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        const int SCREENSHOT_TIMER_MS = 200;
        int deathCounter = 0;
        Game GAME = Game.LIES_OF_P;
        Dictionary<Game, List<string>> deathTexts = new Dictionary<Game, List<string>>
        {
            { Game.SEKIRO, new List<string>{ { "YOU ARE DEAD" } } },
            { Game.LIES_OF_P, new List < string > { "IE" } } 
        };

        [Flags]
        public enum Modifiers
        {
            NoMod = 0x0000,
            Alt = 0x0001,
            Ctrl = 0x0002,
            Shift = 0x0004,
            Win = 0x0008
        }

        enum Game {
            SEKIRO,
            LIES_OF_P,
            LIES_OF_P2
        }

        enum Hotkeys {
            Increment,
            Increment2,
            Decrement,
            Decrement2,
            Reset,
            Reset2,
            Quit,
            Quit2,
            Test,
        }

        private NotifyIcon notifyIcon = new NotifyIcon();
        StringBuilder trayText = new StringBuilder();

        const int DEATH_INCREMENT_COOLDOWN = 5;
        int deathIncrementCooldownTimer = 0;
        bool isDraggingWindow = false;

        const string LOP_DEATHCOUNT_BYTE_TEXT = "YouDieCount";
        const int LOP_DEATHCOUNT_BYTE_OFFSET = 26;
        byte[] matchBytes = Encoding.ASCII.GetBytes(LOP_DEATHCOUNT_BYTE_TEXT);
        FileSystemWatcher fileSystemWatcher = new FileSystemWatcher();

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();
            SetupTrayIcon();
            LiesOfPSaveFileMonitor();
#if !LIES_OF_P
            StartOCRLoop();
#endif
        }

        async void LiesOfPSaveFileMonitor()
        {
            Trace.WriteLine("Waiting for game to launch...");

            Process[] processes = Array.Empty<Process>();
            Process process;
            do
            {
                processes = Process.GetProcessesByName("LOP");
                await Task.Delay(2000);
            } while (processes.Length == 0);

            process = processes[0];
            string? filePath = GetProcessFilename(process);
            StringBuilder saveFolder = new StringBuilder(Path.GetDirectoryName(filePath));
            saveFolder.Append("\\LiesofP\\Saved\\SaveGames\\");

            fileSystemWatcher.Path = saveFolder.ToString();
            fileSystemWatcher.Filter = "SaveData*.*";
            fileSystemWatcher.IncludeSubdirectories = true;
            fileSystemWatcher.NotifyFilter = NotifyFilters.LastWrite;

            fileSystemWatcher.Changed += OnChanged;

            fileSystemWatcher.EnableRaisingEvents = true;
            Trace.WriteLine("Game detected. Listening for file changes...");
        }

        async void OnChanged(object source, FileSystemEventArgs e)
        {
            Trace.WriteLine("File change detected\n");
            fileSystemWatcher.EnableRaisingEvents = false;
            
            try
            {
                using (var fs = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int i = 0;
                    int readByte;

                    while ((readByte = fs.ReadByte()) != -1)
                    {
                        if (matchBytes[i] == readByte)
                        {
                            i++;
                        }
                        else
                        {
                            i = 0;
                        }
                        if (i == matchBytes.Length)
                        {
                            Trace.WriteLine($"YouDieCount found at {fs.Position}.");

                            fs.Seek(LOP_DEATHCOUNT_BYTE_OFFSET, SeekOrigin.Current);
                            using (var br = new BinaryReader(fs, Encoding.ASCII)) 
                            {
                                deathCounter = br.ReadInt32();
                                UpdateCounter();
                                fileSystemWatcher.EnableRaisingEvents = true;
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) 
            {
                Trace.WriteLine("File currently in use, waiting and trying again...");
                await Task.Delay(500);
                OnChanged(source, e);
            }
        }

#if !LIES_OF_P
        void StartOCRLoop()
        {
            System.Threading.Timer screenshotTimer = new System.Threading.Timer(new System.Threading.TimerCallback(TakeScreenshot));
            screenshotTimer.Change(0, SCREENSHOT_TIMER_MS);
        }

        void TakeScreenshot(object? obj)
        {
            if (deathIncrementCooldownTimer > 0 || isDraggingWindow)
            {
                deathIncrementCooldownTimer--;
                return;
            }

            double halfScreenWidth = SystemParameters.PrimaryScreenWidth * .45f;
            double halfScreenHeight = SystemParameters.PrimaryScreenHeight * .2f;

            using (Bitmap bmp = new Bitmap((int)halfScreenWidth, (int)halfScreenHeight))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen((int)(SystemParameters.PrimaryScreenWidth * .3), (int)(SystemParameters.PrimaryScreenHeight * .4166), 0, 0, bmp.Size);
                    //g.CopyFromScreen((int)(SystemParameters.PrimaryScreenWidth * .4), (int)(SystemParameters.PrimaryScreenHeight * .4166), 
                    //                 (int)(SystemParameters.PrimaryScreenWidth * .3), (int)(SystemParameters.PrimaryScreenHeight * .347), bmp.Size);

                    using (MemoryStream memoryStream = new MemoryStream())
                    {
                        // Invert bitmap before sending to OCR for better clarity
                        //bmp.Save("DeathCounterTemp.bmp");
                        bmp.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Bmp);
                        memoryStream.Seek(0, System.IO.SeekOrigin.Begin);
                        ProcessOCR(memoryStream);
                    }
                }
            }
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
                            String imageText = page.GetText();
                            Trace.WriteLine(imageText);
        
                            foreach (string deathText in deathTexts[GAME])
                            {
                                if (imageText.Contains(deathText))
                                {
                                    ModifyCounter((int)Hotkeys.Increment);
                                    deathIncrementCooldownTimer = DEATH_INCREMENT_COOLDOWN;
                                }
                            }
                        }
                    }
                }
            }
        }
#endif
        private void SetupTrayIcon()
        {
            trayText.AppendLine("      Death Counter");
            trayText.AppendLine("Increment: Ctrl + Add ");
            trayText.AppendLine("Decrement: Ctrl + Subtract");
            trayText.AppendLine("Reset: Ctrl + 0 ");
            trayText.Append("Quit: Ctrl + .");

            notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
            notifyIcon.Text = trayText.ToString();
            notifyIcon.MouseClick += Handle_TooltipMouseClick;
            notifyIcon.Visible = true;

            ShowInTaskbar = false;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            //RemoveFromAltTab();
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

            RegisterHotKey(new WindowInteropHelper(this).Handle, (int)Hotkeys.Increment, (int)Modifiers.Ctrl, (int)Keys.Oemplus);
            RegisterHotKey(new WindowInteropHelper(this).Handle, (int)Hotkeys.Increment2, (int)Modifiers.Ctrl, (int)Keys.Add);
            RegisterHotKey(new WindowInteropHelper(this).Handle, (int)Hotkeys.Decrement, (int)Modifiers.Ctrl, (int)Keys.OemMinus);
            RegisterHotKey(new WindowInteropHelper(this).Handle, (int)Hotkeys.Decrement2, (int)Modifiers.Ctrl, (int)Keys.Subtract);
            RegisterHotKey(new WindowInteropHelper(this).Handle, (int)Hotkeys.Reset, (int)Modifiers.Ctrl, (int)Keys.NumPad0);
            RegisterHotKey(new WindowInteropHelper(this).Handle, (int)Hotkeys.Reset2, (int)Modifiers.Ctrl, (int)Keys.D0);
            RegisterHotKey(new WindowInteropHelper(this).Handle, (int)Hotkeys.Quit, (int)Modifiers.Ctrl, (int)Keys.OemPeriod);
            RegisterHotKey(new WindowInteropHelper(this).Handle, (int)Hotkeys.Quit2, (int)Modifiers.Ctrl, (int)Keys.Decimal);
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
                case (int)Hotkeys.Increment2:
                    deathCounter++;
                    break;
                case (int)Hotkeys.Decrement:
                case (int)Hotkeys.Decrement2:
                    deathCounter--;
                    break;
                case (int)Hotkeys.Reset:
                case (int)Hotkeys.Reset2:
                    deathCounter = 0;
                    break;
                case (int)Hotkeys.Quit:
                case (int)Hotkeys.Quit2:
                    Close();
                    break;
#if !LIES_OF_P
                case (int)Hotkeys.Test:
                    TakeScreenshot(null);
                    break;
#endif
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
            fileSystemWatcher.Changed -= OnChanged;
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

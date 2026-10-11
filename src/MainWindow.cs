using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.ServiceProcess;

namespace WuLocker
{
    public class DiagnosticItem
    {
        public string CategoryKey;
        public string CategoryName;
        public string Id;
        public string Description;
        public string StatusText;
        public bool IsBlocked;
    }

    public class MainWindow : Form
    {
        private Panel headerPanel;
        private Label titleLabel;
        private Button closeButton;
        private Button minButton;

        private Panel statusCard;
        private Label statusHeaderLabel;
        private Label statusTextLabel;
        private PictureBox statusIndicator;
        private Button btnDetails;

        private CustomButton btnBlock;
        private CustomButton btnEnable;
        private CustomButton btnRepair;
        private CheckBox chkProtect;
        private CheckBox chkStore;
        private ComboBox comboLang;

        private Timer statusTimer;
        private LockerEngine engine;
        private NotifyIcon trayIcon;
        private ContextMenu trayMenu;

        // Colors
        private static readonly Color ColorBg = Color.FromArgb(15, 23, 42); // slate-900
        private static readonly Color ColorCard = Color.FromArgb(30, 41, 59); // slate-800
        private static readonly Color ColorText = Color.FromArgb(241, 245, 249); // slate-100
        private static readonly Color ColorTextMuted = Color.FromArgb(148, 163, 184); // slate-400
        private static readonly Color ColorGreen = Color.FromArgb(16, 185, 129); // emerald-500
        private static readonly Color ColorRed = Color.FromArgb(239, 68, 68); // red-500
        private static readonly Color ColorBlue = Color.FromArgb(59, 130, 246); // blue-500

        // Custom Dragging
        private bool dragging = false;
        private Point dragCursorPoint;
        private Point dragFormPoint;

        private string lang = "TR";
        private bool updatesBlocked = false;

        // Localization Variables
        private string txtStatusChecking;
        private string txtStatusBlocked;
        private string txtStatusActive;
        private string txtStatusUnknown;
        private string txtSuccessBlock;
        private string txtSuccessEnable;
        private string txtErrorTitle;
        private string txtSuccessTitle;
        private string txtRepairingStatus;
        private string txtSuccessRepair;
        private string txtRepairRebootPrompt;
        private string txtDetailsTitle;
        private string txtTrayOpen;
        private string txtTrayExit;

        [DllImport("kernel32.dll", EntryPoint = "WritePrivateProfileStringW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WritePrivateProfileString(
            string section, string key, string val, string filePath);

        private bool WriteIni(string section, string key, string value)
        {
            try
            {
                return WritePrivateProfileString(section, key, value, engine.IniPath);
            }
            catch
            {
                return false;
            }
        }

        private int ScaleValue(int value)
        {
            try
            {
                using (Graphics graphics = this.CreateGraphics())
                {
                    double scaled = value * graphics.DpiX / 96.0;
                    return Math.Max(1, (int)Math.Round(scaled));
                }
            }
            catch
            {
                return value;
            }
        }

        public MainWindow()
        {
            engine = new LockerEngine();

            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(520, 390);
            this.MinimumSize = new Size(460, 360);
            this.BackColor = ColorBg;
            this.Text = "WuLocker";
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.DoubleBuffered = true;

            try
            {
                this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception ex)
            {
                OperationLog.Error("Uygulama ikonu yuklenemedi.", ex);
            }

            // Header Panel
            headerPanel = new Panel();
            headerPanel.Dock = DockStyle.Top;
            headerPanel.Height = ScaleValue(42);
            headerPanel.BackColor = Color.FromArgb(10, 15, 30);
            headerPanel.MouseDown += HeaderPanel_MouseDown;
            headerPanel.MouseMove += HeaderPanel_MouseMove;
            headerPanel.MouseUp += HeaderPanel_MouseUp;

            TableLayoutPanel headerLayout = new TableLayoutPanel();
            headerLayout.Dock = DockStyle.Fill;
            headerLayout.ColumnCount = 3;
            headerLayout.RowCount = 1;
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleValue(42)));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleValue(42)));
            headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            headerPanel.Controls.Add(headerLayout);

            titleLabel = new Label();
            titleLabel.Text = VersionInfo.ProductName + " v" + VersionInfo.Version;
            titleLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            titleLabel.ForeColor = ColorText;
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            titleLabel.Padding = new Padding(ScaleValue(12), 0, 0, 0);
            titleLabel.AutoEllipsis = true;
            titleLabel.MouseDown += HeaderPanel_MouseDown;
            titleLabel.MouseMove += HeaderPanel_MouseMove;
            titleLabel.MouseUp += HeaderPanel_MouseUp;

            closeButton = new Button();
            closeButton.Text = "X";
            closeButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            closeButton.ForeColor = ColorTextMuted;
            closeButton.Dock = DockStyle.Fill;
            closeButton.Margin = new Padding(0);
            closeButton.FlatStyle = FlatStyle.Flat;
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(220, 38, 38);
            closeButton.Click += (s, e) => this.Close();

            minButton = new Button();
            minButton.Text = "_";
            minButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            minButton.ForeColor = ColorTextMuted;
            minButton.Dock = DockStyle.Fill;
            minButton.Margin = new Padding(0);
            minButton.FlatStyle = FlatStyle.Flat;
            minButton.FlatAppearance.BorderSize = 0;
            minButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(51, 65, 85);
            minButton.Click += (s, e) =>
            {
                this.WindowState = FormWindowState.Minimized;
            };

            headerLayout.Controls.Add(titleLabel, 0, 0);
            headerLayout.Controls.Add(minButton, 1, 0);
            headerLayout.Controls.Add(closeButton, 2, 0);
            this.Controls.Add(headerPanel);

            // Main Layout
            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.Padding = new Padding(ScaleValue(15));
            mainLayout.BackColor = ColorBg;
            mainLayout.ColumnCount = 1;
            mainLayout.RowCount = 5;
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // Status card
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleValue(54))); // Action Buttons
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleValue(30))); // chkProtect
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleValue(30))); // chkStore
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleValue(36))); // Bottom / Lang
            this.Controls.Add(mainLayout);
            headerPanel.BringToFront();

            // Status Card
            statusCard = new Panel();
            statusCard.Dock = DockStyle.Fill;
            statusCard.Margin = new Padding(0, 0, 0, ScaleValue(12));
            statusCard.Padding = new Padding(ScaleValue(14));
            statusCard.BackColor = ColorCard;
            statusCard.Paint += StatusCard_Paint;

            TableLayoutPanel statusLayout = new TableLayoutPanel();
            statusLayout.Dock = DockStyle.Fill;
            statusLayout.ColumnCount = 3;
            statusLayout.RowCount = 2;
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleValue(60)));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleValue(80)));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
            statusCard.Controls.Add(statusLayout);

            statusIndicator = new PictureBox();
            statusIndicator.Dock = DockStyle.Fill;
            statusIndicator.Margin = new Padding(0, 0, ScaleValue(10), 0);
            statusIndicator.Paint += StatusIndicator_Paint;

            statusHeaderLabel = new Label();
            statusHeaderLabel.Text = "UPDATE STATUS";
            statusHeaderLabel.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            statusHeaderLabel.ForeColor = ColorTextMuted;
            statusHeaderLabel.Dock = DockStyle.Fill;
            statusHeaderLabel.TextAlign = ContentAlignment.BottomLeft;
            statusHeaderLabel.AutoEllipsis = true;

            statusTextLabel = new Label();
            statusTextLabel.Text = "Checking status...";
            statusTextLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            statusTextLabel.ForeColor = ColorText;
            statusTextLabel.Dock = DockStyle.Fill;
            statusTextLabel.TextAlign = ContentAlignment.TopLeft;
            statusTextLabel.AutoEllipsis = true;

            btnDetails = new Button();
            btnDetails.Text = "Details";
            btnDetails.Font = new Font("Segoe UI", 8F, FontStyle.Regular);
            btnDetails.ForeColor = ColorTextMuted;
            btnDetails.BackColor = Color.FromArgb(45, 55, 75);
            btnDetails.FlatStyle = FlatStyle.Flat;
            btnDetails.FlatAppearance.BorderSize = 0;
            btnDetails.Cursor = Cursors.Hand;
            btnDetails.Dock = DockStyle.Fill;
            btnDetails.Margin = new Padding(0, ScaleValue(4), 0, ScaleValue(4));
            btnDetails.Click += BtnDetails_Click;

            statusLayout.Controls.Add(statusIndicator, 0, 0);
            statusLayout.SetRowSpan(statusIndicator, 2);
            statusLayout.Controls.Add(statusHeaderLabel, 1, 0);
            statusLayout.Controls.Add(statusTextLabel, 1, 1);
            statusLayout.Controls.Add(btnDetails, 2, 0);
            statusLayout.SetRowSpan(btnDetails, 2);
            mainLayout.Controls.Add(statusCard, 0, 0);

            // Action Buttons
            TableLayoutPanel buttonLayout = new TableLayoutPanel();
            buttonLayout.Dock = DockStyle.Fill;
            buttonLayout.ColumnCount = 3;
            buttonLayout.RowCount = 1;
            buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
            buttonLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.Controls.Add(buttonLayout, 0, 1);

            btnBlock = new CustomButton();
            btnBlock.Text = "Lock Updates";
            btnBlock.Dock = DockStyle.Fill;
            btnBlock.Margin = new Padding(0, 0, ScaleValue(8), ScaleValue(8));
            btnBlock.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnBlock.BackColor = ColorRed;
            btnBlock.ForeColor = Color.White;
            btnBlock.Click += BtnBlock_Click;

            btnEnable = new CustomButton();
            btnEnable.Text = "Enable Updates";
            btnEnable.Dock = DockStyle.Fill;
            btnEnable.Margin = new Padding(0, 0, ScaleValue(8), ScaleValue(8));
            btnEnable.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnEnable.BackColor = ColorGreen;
            btnEnable.ForeColor = Color.White;
            btnEnable.Click += BtnEnable_Click;

            btnRepair = new CustomButton();
            btnRepair.Text = "Repair";
            btnRepair.Dock = DockStyle.Fill;
            btnRepair.Margin = new Padding(0, 0, 0, ScaleValue(8));
            btnRepair.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnRepair.BackColor = ColorBlue;
            btnRepair.ForeColor = Color.White;
            btnRepair.Click += BtnRepair_Click;

            buttonLayout.Controls.Add(btnBlock, 0, 0);
            buttonLayout.Controls.Add(btnEnable, 1, 0);
            buttonLayout.Controls.Add(btnRepair, 2, 0);

            // chkProtect
            chkProtect = new CheckBox();
            chkProtect.Text = "Lock Service Settings (Strong Protection)";
            chkProtect.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            chkProtect.ForeColor = ColorTextMuted;
            chkProtect.Dock = DockStyle.Fill;
            chkProtect.AutoSize = false;
            chkProtect.AutoEllipsis = true;
            chkProtect.Margin = new Padding(0, 0, 0, ScaleValue(4));
            mainLayout.Controls.Add(chkProtect, 0, 2);

            // chkStore (Allow Microsoft Store & Xbox Apps)
            chkStore = new CheckBox();
            chkStore.Text = "Allow Microsoft Store & Xbox Apps";
            chkStore.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            chkStore.ForeColor = ColorTextMuted;
            chkStore.Dock = DockStyle.Fill;
            chkStore.AutoSize = false;
            chkStore.AutoEllipsis = true;
            chkStore.Margin = new Padding(0, 0, 0, ScaleValue(4));
            mainLayout.Controls.Add(chkStore, 0, 3);

            // Bottom Panel
            Panel bottomPanel = new Panel();
            bottomPanel.Dock = DockStyle.Fill;
            bottomPanel.Margin = new Padding(0);
            mainLayout.Controls.Add(bottomPanel, 0, 4);

            comboLang = new ComboBox();
            comboLang.DropDownStyle = ComboBoxStyle.DropDownList;
            comboLang.FlatStyle = FlatStyle.Flat;
            comboLang.BackColor = ColorCard;
            comboLang.ForeColor = ColorText;
            comboLang.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            comboLang.Width = ScaleValue(76);
            comboLang.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            comboLang.Location = new Point(0, ScaleValue(4));
            bottomPanel.Controls.Add(comboLang);
            bottomPanel.Resize += delegate(object sender, EventArgs e)
            {
                comboLang.Left = bottomPanel.ClientSize.Width - comboLang.Width;
            };

            foreach (var l in LocalizationManager.SupportedLanguages)
            {
                comboLang.Items.Add(l);
            }

            string currentLang = AppConfig.ReadString(engine.IniPath, "Settings", "Language", "Auto", 64);
            if (currentLang.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                currentLang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToUpper();
            }
            else
            {
                currentLang = currentLang.ToUpper();
            }

            if (Array.IndexOf(LocalizationManager.SupportedLanguages, currentLang) == -1)
            {
                currentLang = "EN";
            }

            comboLang.SelectedItem = currentLang;
            comboLang.SelectedIndexChanged += ComboLang_SelectedIndexChanged;

            ApplyLocalization();
            LoadInitialSettings();

            chkProtect.CheckedChanged += (s, e) =>
            {
                engine.Config.LockServicesOnBlock = chkProtect.Checked;
                WriteIni("Settings", "LockServicesOnBlock", chkProtect.Checked ? "1" : "0");
            };

            chkStore.CheckedChanged += (s, e) =>
            {
                engine.Config.AllowMicrosoftStore = chkStore.Checked;
                WriteIni("Settings", "AllowMicrosoftStore", chkStore.Checked ? "1" : "0");
                UpdateStatusGUI();
            };

            // Setup System Tray
            SetupSystemTray();

            // Status Check Timer
            statusTimer = new Timer();
            statusTimer.Interval = 4000;
            statusTimer.Tick += (s, e) => UpdateStatusGUI();
            statusTimer.Start();

            UpdateStatusGUI();
        }

        private void SetupSystemTray()
        {
            try
            {
                trayMenu = new ContextMenu();
                MenuItem itemOpen = new MenuItem(txtTrayOpen != null ? txtTrayOpen : "Show / Open", (s, e) => RestoreFromTray());
                MenuItem itemBlock = new MenuItem(btnBlock.Text, (s, e) => BtnBlock_Click(s, e));
                MenuItem itemEnable = new MenuItem(btnEnable.Text, (s, e) => BtnEnable_Click(s, e));
                MenuItem itemSep = new MenuItem("-");
                MenuItem itemExit = new MenuItem(txtTrayExit != null ? txtTrayExit : "Exit", (s, e) =>
                {
                    if (trayIcon != null) trayIcon.Visible = false;
                    Application.Exit();
                });

                trayMenu.MenuItems.Add(itemOpen);
                trayMenu.MenuItems.Add(itemBlock);
                trayMenu.MenuItems.Add(itemEnable);
                trayMenu.MenuItems.Add(itemSep);
                trayMenu.MenuItems.Add(itemExit);

                trayIcon = new NotifyIcon();
                trayIcon.Text = "WuLocker";
                trayIcon.Icon = this.Icon;
                trayIcon.ContextMenu = trayMenu;
                trayIcon.Visible = true;
                trayIcon.DoubleClick += (s, e) => RestoreFromTray();
            }
            catch (Exception ex)
            {
                OperationLog.Error("Sistem tepsisi hazirlanamadi.", ex);
            }
        }

        private void RestoreFromTray()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
            this.Activate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
            base.OnFormClosing(e);
        }

        private void ApplyLocalization()
        {
            try
            {
                lang = AppConfig.ReadString(engine.IniPath, "Settings", "Language", "Auto", 64);
                var dict = Program.LoadLocalization(lang);

                this.Text = VersionInfo.ProductName;
                titleLabel.Text = VersionInfo.ProductName + " v" + VersionInfo.Version;

                statusHeaderLabel.Text = LocalizationManager.Get(dict, "StatusHeader", "UPDATE STATUS");
                btnBlock.Text = LocalizationManager.Get(dict, "BtnBlock", "Lock Updates");
                btnEnable.Text = LocalizationManager.Get(dict, "BtnEnable", "Enable Updates");
                btnRepair.Text = LocalizationManager.Get(dict, "BtnRepair", "Repair Updates");
                btnDetails.Text = LocalizationManager.Get(dict, "BtnDetails", "Details");
                chkProtect.Text = LocalizationManager.Get(dict, "ChkProtect", "Lock Service Settings (Strong Protection)");
                chkStore.Text = LocalizationManager.Get(dict, "ChkStore", "Allow Microsoft Store & Xbox Apps");

                txtStatusChecking = LocalizationManager.Get(dict, "StatusChecking", "Checking status...");
                txtStatusBlocked = LocalizationManager.Get(dict, "StatusBlocked", "System Updates Disabled and Locked");
                txtStatusActive = LocalizationManager.Get(dict, "StatusActive", "System Updates Active and Running");
                txtStatusUnknown = LocalizationManager.Get(dict, "StatusUnknown", "Status Unknown");
                txtSuccessBlock = LocalizationManager.Get(dict, "SuccessBlock", "System Updates successfully disabled and protected!");
                txtSuccessEnable = LocalizationManager.Get(dict, "SuccessEnable", "System Updates successfully enabled!");
                txtErrorTitle = LocalizationManager.Get(dict, "ErrorTitle", "Error");
                txtSuccessTitle = LocalizationManager.Get(dict, "SuccessTitle", "Success");
                txtRepairingStatus = LocalizationManager.Get(dict, "RepairingStatus", "Repairing: {0}");
                txtSuccessRepair = LocalizationManager.Get(dict, "SuccessRepair", "Windows Update components have been successfully reset and repaired!");
                txtRepairRebootPrompt = LocalizationManager.Get(dict, "RepairRebootPrompt", "Would you like to restart your computer now to apply the changes?");
                txtDetailsTitle = LocalizationManager.Get(dict, "DetailsTitle", "Component Status Details");
                txtTrayOpen = LocalizationManager.Get(dict, "TrayOpen", "Show / Open");
                txtTrayExit = LocalizationManager.Get(dict, "TrayExit", "Exit");

                if (trayMenu != null && trayMenu.MenuItems.Count >= 5)
                {
                    trayMenu.MenuItems[0].Text = txtTrayOpen;
                    trayMenu.MenuItems[1].Text = btnBlock.Text;
                    trayMenu.MenuItems[2].Text = btnEnable.Text;
                    trayMenu.MenuItems[4].Text = txtTrayExit;
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Yerellestirme uygulanamadi.", ex);
            }
        }

        private void LoadInitialSettings()
        {
            try
            {
                if (engine.IsServiceDisabledForStatus("wuauserv"))
                {
                    chkProtect.Checked = engine.IsServiceRegistryLockedForStatus("wuauserv");
                }
                else
                {
                    chkProtect.Checked = engine.Config.LockServicesOnBlock;
                }
                chkStore.Checked = engine.Config.AllowMicrosoftStore;
            }
            catch
            {
                chkProtect.Checked = engine.Config.LockServicesOnBlock;
                chkStore.Checked = engine.Config.AllowMicrosoftStore;
            }
        }

        private void HeaderPanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                dragCursorPoint = Cursor.Position;
                dragFormPoint = this.Location;
            }
        }

        private void HeaderPanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (dragging)
            {
                Point diff = Point.Subtract(Cursor.Position, new Size(dragCursorPoint));
                this.Location = Point.Add(dragFormPoint, new Size(diff));
            }
        }

        private void HeaderPanel_MouseUp(object sender, MouseEventArgs e)
        {
            dragging = false;
        }

        private void StatusCard_Paint(object sender, PaintEventArgs e)
        {
            using (Pen borderPen = new Pen(Color.FromArgb(51, 65, 85), 1))
            {
                e.Graphics.DrawRectangle(borderPen, 0, 0, statusCard.Width - 1, statusCard.Height - 1);
            }
        }

        private void StatusIndicator_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color circleColor = ColorBlue;
            Color pulseColor = Color.FromArgb(40, ColorBlue);

            if (updatesBlocked)
            {
                circleColor = ColorRed;
                pulseColor = Color.FromArgb(45, ColorRed);
            }
            else
            {
                circleColor = ColorGreen;
                pulseColor = Color.FromArgb(45, ColorGreen);
            }

            int w = statusIndicator.Width;
            int h = statusIndicator.Height;
            int size = Math.Min(w, h) - ScaleValue(8);
            if (size <= 0) size = 10;
            int x = (w - size) / 2;
            int y = (h - size) / 2;

            Rectangle outerRect = new Rectangle(x, y, size, size);
            using (SolidBrush brush = new SolidBrush(pulseColor))
            {
                e.Graphics.FillEllipse(brush, outerRect);
            }

            int innerSize = (int)(size * 0.58);
            int innerX = (w - innerSize) / 2;
            int innerY = (h - innerSize) / 2;

            Rectangle innerRect = new Rectangle(innerX, innerY, innerSize, innerSize);
            using (SolidBrush brush = new SolidBrush(circleColor))
            {
                e.Graphics.FillEllipse(brush, innerRect);
            }
        }

        private void UpdateStatusGUI()
        {
            try
            {
                bool isBlocked = engine.IsUpdatesBlocked();
                updatesBlocked = isBlocked;

                if (isBlocked)
                {
                    statusTextLabel.Text = txtStatusBlocked;
                    statusTextLabel.ForeColor = ColorRed;
                }
                else
                {
                    statusTextLabel.Text = txtStatusActive;
                    statusTextLabel.ForeColor = ColorGreen;
                }
                statusIndicator.Invalidate();

                if (trayIcon != null)
                {
                    string statusTooltip = isBlocked ? "WuLocker: Updates Blocked" : "WuLocker: Updates Active";
                    if (statusTooltip.Length >= 64) statusTooltip = statusTooltip.Substring(0, 63);
                    trayIcon.Text = statusTooltip;
                }
            }
            catch
            {
                statusTextLabel.Text = txtStatusUnknown;
                statusTextLabel.ForeColor = ColorBlue;
            }
        }

        private void EnableFormControls(bool enable)
        {
            btnBlock.Enabled = enable;
            btnEnable.Enabled = enable;
            btnRepair.Enabled = enable;
            chkProtect.Enabled = enable;
            chkStore.Enabled = enable;
            btnDetails.Enabled = enable;
            comboLang.Enabled = enable;
        }

        private void BtnBlock_Click(object sender, EventArgs e)
        {
            this.Cursor = Cursors.WaitCursor;
            EnableFormControls(false);

            bool protect = chkProtect.Checked;
            System.Threading.Thread thread = new System.Threading.Thread(() =>
            {
                try
                {
                    engine.ExecuteBlock(protect);
                    this.BeginInvoke(new Action(() =>
                    {
                        this.Cursor = Cursors.Default;
                        EnableFormControls(true);
                        UpdateStatusGUI();
                    }));
                }
                catch (Exception ex)
                {
                    OperationLog.Error("GUI block islemi basarisiz.", ex);
                    this.BeginInvoke(new Action(() =>
                    {
                        this.Cursor = Cursors.Default;
                        EnableFormControls(true);
                        UpdateStatusGUI();
                        MessageBox.Show(txtErrorTitle + ": " + ex.Message, txtErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void BtnEnable_Click(object sender, EventArgs e)
        {
            this.Cursor = Cursors.WaitCursor;
            EnableFormControls(false);

            System.Threading.Thread thread = new System.Threading.Thread(() =>
            {
                try
                {
                    engine.ExecuteEnable();
                    this.BeginInvoke(new Action(() =>
                    {
                        this.Cursor = Cursors.Default;
                        EnableFormControls(true);
                        UpdateStatusGUI();
                    }));
                }
                catch (Exception ex)
                {
                    OperationLog.Error("GUI enable islemi basarisiz.", ex);
                    this.BeginInvoke(new Action(() =>
                    {
                        this.Cursor = Cursors.Default;
                        EnableFormControls(true);
                        UpdateStatusGUI();
                        MessageBox.Show(txtErrorTitle + ": " + ex.Message, txtErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void BtnRepair_Click(object sender, EventArgs e)
        {
            ShowRepairDialog();
        }

        private void ShowRepairDialog()
        {
            string currentLang = lang;
            Dictionary<string, string> dict = Program.LoadLocalization(currentLang);

            string title = LocalizationManager.Get(dict, "RepairWizardTitle", "Windows Update Repair Wizard");
            string desc = LocalizationManager.Get(dict, "RepairWizardDesc", "Reset and repair Windows Update services, caches, and configuration.");
            string optQuick = LocalizationManager.Get(dict, "RepairOptQuick", "Quick Component Reset (Recommended - ~5 seconds)");
            string optQuickDesc = LocalizationManager.Get(dict, "RepairOptQuickDesc", "Resets services, caches (SoftwareDistribution), BITS queue, network/proxy, and DLLs.");
            string optDeep = LocalizationManager.Get(dict, "RepairOptDeep", "Deep System File Scan (SFC & DISM - May take 15-30 minutes)");
            string optDeepDesc = LocalizationManager.Get(dict, "RepairOptDeepDesc", "Scans and repairs corrupted Windows system files and component store thoroughly.");
            string btnStartText = LocalizationManager.Get(dict, "RepairBtnStart", "Start Repair");
            string btnRebootText = LocalizationManager.Get(dict, "RepairBtnReboot", "Restart System");
            string btnCloseText = LocalizationManager.Get(dict, "RepairBtnClose", "Close");
            string statusReadyText = LocalizationManager.Get(dict, "RepairStatusReady", "Ready for repair. Click the button to start.");
            string statusRunningText = LocalizationManager.Get(dict, "RepairStatusRunning", "Repair in progress... Please wait.");
            string statusCompletedText = LocalizationManager.Get(dict, "RepairStatusCompleted", "Repair completed successfully!");
            string logHeader = LocalizationManager.Get(dict, "RepairLogHeader", "Live Operation Log");

            using (Form modal = new Form())
            {
                modal.Text = title;
                modal.StartPosition = FormStartPosition.CenterParent;
                modal.ClientSize = new Size(ScaleValue(640), ScaleValue(540));
                modal.MinimumSize = new Size(ScaleValue(560), ScaleValue(460));
                modal.BackColor = ColorBg;
                modal.ForeColor = ColorText;
                modal.FormBorderStyle = FormBorderStyle.Sizable;
                modal.MaximizeBox = true;
                modal.MinimizeBox = false;
                modal.ShowInTaskbar = false;
                modal.Font = new Font("Segoe UI", 9F);
                modal.Icon = this.Icon;
                modal.KeyPreview = true;

                // Root TableLayoutPanel (1 column, 4 rows)
                TableLayoutPanel rootLayout = new TableLayoutPanel();
                rootLayout.Dock = DockStyle.Fill;
                rootLayout.ColumnCount = 1;
                rootLayout.RowCount = 4;
                rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Row 0: Header Banner
                rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Row 1: Repair Options Card
                rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));      // Row 2: Live Progress & Log Terminal
                rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Row 3: Action Buttons Footer
                rootLayout.Margin = new Padding(0);
                rootLayout.Padding = new Padding(0);
                modal.Controls.Add(rootLayout);

                // ================= ROW 0: TOP HEADER BANNER =================
                Panel topPanel = new Panel();
                topPanel.Dock = DockStyle.Fill;
                topPanel.AutoSize = true;
                topPanel.BackColor = Color.FromArgb(12, 19, 36);
                topPanel.Margin = new Padding(0);
                topPanel.Padding = new Padding(ScaleValue(16), ScaleValue(12), ScaleValue(16), ScaleValue(12));

                TableLayoutPanel topLayout = new TableLayoutPanel();
                topLayout.Dock = DockStyle.Fill;
                topLayout.AutoSize = true;
                topLayout.ColumnCount = 1;
                topLayout.RowCount = 2;
                topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                topLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                topLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                topPanel.Controls.Add(topLayout);

                Label lblHeaderTitle = new Label();
                lblHeaderTitle.Text = title;
                lblHeaderTitle.Font = new Font("Segoe UI", 11.5F, FontStyle.Bold);
                lblHeaderTitle.ForeColor = ColorText;
                lblHeaderTitle.AutoSize = true;
                lblHeaderTitle.Margin = new Padding(0, 0, 0, ScaleValue(3));

                Label lblHeaderDesc = new Label();
                lblHeaderDesc.Text = desc;
                lblHeaderDesc.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular);
                lblHeaderDesc.ForeColor = ColorTextMuted;
                lblHeaderDesc.AutoSize = true;
                lblHeaderDesc.Margin = new Padding(0);

                topLayout.Controls.Add(lblHeaderTitle, 0, 0);
                topLayout.Controls.Add(lblHeaderDesc, 0, 1);
                rootLayout.Controls.Add(topPanel, 0, 0);

                // ================= ROW 1: REPAIR OPTIONS CARD =================
                Panel optCard = new Panel();
                optCard.Dock = DockStyle.Fill;
                optCard.AutoSize = true;
                optCard.BackColor = ColorCard;
                optCard.Margin = new Padding(ScaleValue(12), ScaleValue(10), ScaleValue(12), ScaleValue(6));
                optCard.Padding = new Padding(ScaleValue(14), ScaleValue(10), ScaleValue(14), ScaleValue(10));

                TableLayoutPanel optLayout = new TableLayoutPanel();
                optLayout.Dock = DockStyle.Fill;
                optLayout.AutoSize = true;
                optLayout.ColumnCount = 1;
                optLayout.RowCount = 2;
                optLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                optLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                optLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                optCard.Controls.Add(optLayout);

                CheckBox chkDeepScan = new CheckBox();
                chkDeepScan.Text = optDeep;
                chkDeepScan.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                chkDeepScan.ForeColor = ColorText;
                chkDeepScan.Checked = false;
                chkDeepScan.AutoSize = true;
                chkDeepScan.Cursor = Cursors.Hand;
                chkDeepScan.Margin = new Padding(0, 0, 0, ScaleValue(2));

                Label lblOptHint = new Label();
                lblOptHint.Text = optDeepDesc;
                lblOptHint.Font = new Font("Segoe UI", 8F, FontStyle.Regular);
                lblOptHint.ForeColor = ColorTextMuted;
                lblOptHint.AutoSize = true;
                lblOptHint.Margin = new Padding(ScaleValue(20), 0, 0, 0);

                optLayout.Controls.Add(chkDeepScan, 0, 0);
                optLayout.Controls.Add(lblOptHint, 0, 1);
                rootLayout.Controls.Add(optCard, 0, 1);

                // ================= ROW 2: PROGRESS & LOG TERMINAL =================
                TableLayoutPanel midLayout = new TableLayoutPanel();
                midLayout.Dock = DockStyle.Fill;
                midLayout.Margin = new Padding(ScaleValue(12), ScaleValue(6), ScaleValue(12), ScaleValue(6));
                midLayout.ColumnCount = 1;
                midLayout.RowCount = 4;
                midLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                midLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Active Step Label
                midLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleValue(12))); // ProgressBar
                midLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Log Header Label
                midLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));      // Terminal Box
                rootLayout.Controls.Add(midLayout, 0, 2);

                Label lblStepStatus = new Label();
                lblStepStatus.Text = statusReadyText;
                lblStepStatus.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
                lblStepStatus.ForeColor = ColorBlue;
                lblStepStatus.AutoSize = true;
                lblStepStatus.Margin = new Padding(0, 0, 0, ScaleValue(6));

                ProgressBar pBar = new ProgressBar();
                pBar.Dock = DockStyle.Fill;
                pBar.Margin = new Padding(0, 0, 0, ScaleValue(8));
                pBar.Style = ProgressBarStyle.Blocks;
                pBar.Value = 0;

                Label lblLogHeader = new Label();
                lblLogHeader.Text = logHeader + ":";
                lblLogHeader.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
                lblLogHeader.ForeColor = ColorTextMuted;
                lblLogHeader.AutoSize = true;
                lblLogHeader.Margin = new Padding(0, 0, 0, ScaleValue(4));

                TextBox txtLog = new TextBox();
                txtLog.Dock = DockStyle.Fill;
                txtLog.Multiline = true;
                txtLog.ReadOnly = true;
                txtLog.ScrollBars = ScrollBars.Vertical;
                txtLog.BackColor = Color.FromArgb(10, 15, 26);
                txtLog.ForeColor = Color.FromArgb(148, 163, 184);
                txtLog.Font = new Font("Consolas", 8.5F, FontStyle.Regular);
                txtLog.BorderStyle = BorderStyle.FixedSingle;
                txtLog.Margin = new Padding(0);

                midLayout.Controls.Add(lblStepStatus, 0, 0);
                midLayout.Controls.Add(pBar, 0, 1);
                midLayout.Controls.Add(lblLogHeader, 0, 2);
                midLayout.Controls.Add(txtLog, 0, 3);

                // ================= ROW 3: FOOTER ACTIONS =================
                Panel bottomPanel = new Panel();
                bottomPanel.Dock = DockStyle.Fill;
                bottomPanel.AutoSize = true;
                bottomPanel.BackColor = Color.FromArgb(10, 15, 30);
                bottomPanel.Padding = new Padding(ScaleValue(12), ScaleValue(10), ScaleValue(12), ScaleValue(10));

                TableLayoutPanel botLayout = new TableLayoutPanel();
                botLayout.Dock = DockStyle.Fill;
                botLayout.AutoSize = true;
                botLayout.ColumnCount = 3;
                botLayout.RowCount = 1;
                botLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                botLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                botLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                bottomPanel.Controls.Add(botLayout);

                Button btnStart = new Button();
                btnStart.Text = btnStartText;
                btnStart.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                btnStart.ForeColor = Color.White;
                btnStart.BackColor = ColorBlue;
                btnStart.FlatStyle = FlatStyle.Flat;
                btnStart.FlatAppearance.BorderSize = 0;
                btnStart.Height = ScaleValue(34);
                btnStart.AutoSize = true;
                btnStart.Padding = new Padding(ScaleValue(14), 0, ScaleValue(14), 0);
                btnStart.Cursor = Cursors.Hand;
                btnStart.Margin = new Padding(0, 0, ScaleValue(8), 0);

                Button btnReboot = new Button();
                btnReboot.Text = btnRebootText;
                btnReboot.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                btnReboot.ForeColor = Color.White;
                btnReboot.BackColor = ColorRed;
                btnReboot.FlatStyle = FlatStyle.Flat;
                btnReboot.FlatAppearance.BorderSize = 0;
                btnReboot.Height = ScaleValue(34);
                btnReboot.AutoSize = true;
                btnReboot.Padding = new Padding(ScaleValue(14), 0, ScaleValue(14), 0);
                btnReboot.Cursor = Cursors.Hand;
                btnReboot.Visible = false;
                btnReboot.Margin = new Padding(0, 0, ScaleValue(8), 0);

                Button btnCloseModal = new Button();
                btnCloseModal.Text = btnCloseText;
                btnCloseModal.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
                btnCloseModal.ForeColor = ColorText;
                btnCloseModal.BackColor = ColorCard;
                btnCloseModal.FlatStyle = FlatStyle.Flat;
                btnCloseModal.FlatAppearance.BorderSize = 0;
                btnCloseModal.Height = ScaleValue(34);
                btnCloseModal.AutoSize = true;
                btnCloseModal.Padding = new Padding(ScaleValue(14), 0, ScaleValue(14), 0);
                btnCloseModal.Cursor = Cursors.Hand;
                btnCloseModal.Margin = new Padding(0);

                FlowLayoutPanel buttonFlow = new FlowLayoutPanel();
                buttonFlow.Dock = DockStyle.Right;
                buttonFlow.AutoSize = true;
                buttonFlow.FlowDirection = FlowDirection.LeftToRight;
                buttonFlow.Margin = new Padding(0);
                buttonFlow.Controls.Add(btnStart);
                buttonFlow.Controls.Add(btnReboot);
                buttonFlow.Controls.Add(btnCloseModal);

                botLayout.Controls.Add(buttonFlow, 2, 0);
                rootLayout.Controls.Add(bottomPanel, 0, 3);

                // Logic & Event Wiring
                bool isRunning = false;
                string confirmExitMsg = LocalizationManager.Get(dict, "RepairConfirmExit", "Onarım işlemi arka planda devam ediyor. Pencereyi kapatmak istiyor musunuz?");
                string logStartedMsg = LocalizationManager.Get(dict, "RepairLogStarted", "Onarım işlemi başlatıldı.");
                string logDeepScanMsg = LocalizationManager.Get(dict, "RepairLogDeepScan", "Derin Tarama");
                string txtYes = LocalizationManager.Get(dict, "StatusValYes", "Evet");
                string txtNo = LocalizationManager.Get(dict, "StatusValNo", "Hayır");

                btnCloseModal.Click += delegate(object s, EventArgs ev)
                {
                    if (isRunning)
                    {
                        DialogResult confirm = MessageBox.Show(
                            confirmExitMsg,
                            "WuLocker",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);
                        if (confirm != DialogResult.Yes) return;
                    }
                    modal.Close();
                };

                modal.FormClosing += delegate(object s, FormClosingEventArgs ev)
                {
                    if (isRunning)
                    {
                        DialogResult confirm = MessageBox.Show(
                            confirmExitMsg,
                            "WuLocker",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);
                        if (confirm != DialogResult.Yes)
                        {
                            ev.Cancel = true;
                            return;
                        }
                    }
                    UpdateStatusGUI();
                };

                btnReboot.Click += delegate(object s, EventArgs ev)
                {
                    DialogResult confirm = MessageBox.Show(
                        txtRepairRebootPrompt,
                        txtSuccessTitle,
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                    if (confirm == DialogResult.Yes)
                    {
                        OperationLog.RunProcess("shutdown.exe", "/r /t 5 /f", 5000);
                        modal.Close();
                    }
                };

                btnStart.Click += delegate(object s, EventArgs ev)
                {
                    bool deep = chkDeepScan.Checked;
                    isRunning = true;
                    btnStart.Enabled = false;
                    chkDeepScan.Enabled = false;
                    pBar.Style = ProgressBarStyle.Marquee;
                    lblStepStatus.Text = statusRunningText;
                    lblStepStatus.ForeColor = ColorBlue;

                    txtLog.AppendText(string.Format("[{0:HH:mm:ss}] {1} ({2}: {3})\r\n", DateTime.Now, logStartedMsg, logDeepScanMsg, deep ? txtYes : txtNo));

                    System.Threading.Thread repairThread = new System.Threading.Thread(() =>
                    {
                        try
                        {
                            engine.ExecuteRepair(
                                delegate(string step)
                                {
                                    if (modal.IsDisposed) return;
                                    modal.BeginInvoke(new Action(() =>
                                    {
                                        lblStepStatus.Text = step;
                                        txtLog.AppendText(string.Format("[{0:HH:mm:ss}] {1}\r\n", DateTime.Now, step));
                                        txtLog.SelectionStart = txtLog.TextLength;
                                        txtLog.ScrollToCaret();
                                    }));
                                },
                                deep,
                                delegate(string line)
                                {
                                    if (modal.IsDisposed) return;
                                    modal.BeginInvoke(new Action(() =>
                                    {
                                        txtLog.AppendText("  " + line + "\r\n");
                                        txtLog.SelectionStart = txtLog.TextLength;
                                        txtLog.ScrollToCaret();
                                    }));
                                },
                                dict
                            );

                            if (modal.IsDisposed) return;
                            modal.BeginInvoke(new Action(() =>
                            {
                                isRunning = false;
                                pBar.Style = ProgressBarStyle.Blocks;
                                pBar.Value = 100;
                                lblStepStatus.Text = statusCompletedText;
                                lblStepStatus.ForeColor = ColorGreen;
                                btnReboot.Visible = true;
                                btnStart.Visible = false;
                                txtLog.AppendText(string.Format("\r\n[{0:HH:mm:ss}] {1}\r\n", DateTime.Now, txtSuccessRepair));
                                txtLog.SelectionStart = txtLog.TextLength;
                                txtLog.ScrollToCaret();
                                UpdateStatusGUI();
                            }));
                        }
                        catch (Exception ex)
                        {
                            OperationLog.Error("Onarim sihirbazi sirasinda hata olustu.", ex);
                            if (modal.IsDisposed) return;
                            modal.BeginInvoke(new Action(() =>
                            {
                                isRunning = false;
                                pBar.Style = ProgressBarStyle.Blocks;
                                lblStepStatus.Text = txtErrorTitle + ": " + ex.Message;
                                lblStepStatus.ForeColor = ColorRed;
                                btnStart.Enabled = true;
                                chkDeepScan.Enabled = true;
                                txtLog.AppendText(string.Format("\r\n[{0:HH:mm:ss}] HATA: {1}\r\n", DateTime.Now, ex.Message));
                                txtLog.SelectionStart = txtLog.TextLength;
                                txtLog.ScrollToCaret();
                                UpdateStatusGUI();
                            }));
                        }
                    });
                    repairThread.IsBackground = true;
                    repairThread.Start();
                };

                modal.ShowDialog(this);
            }
        }

        private string GetDiagnosticItemDescription(Dictionary<string, string> dict, string categoryKey, string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string sanitized = id.Replace(" ", "").Replace(".exe", "").Replace(".EXE", "").Replace("_", "");
            string key = "Desc_" + categoryKey + "_" + sanitized;
            string desc = LocalizationManager.Get(dict, key, "");
            if (!string.IsNullOrEmpty(desc)) return desc;

            key = "Desc_" + categoryKey + "_" + id;
            desc = LocalizationManager.Get(dict, key, "");
            if (!string.IsNullOrEmpty(desc)) return desc;

            if (categoryKey == "svc")
            {
                try
                {
                    using (ServiceController sc = new ServiceController(id))
                    {
                        if (!string.IsNullOrEmpty(sc.DisplayName))
                        {
                            return sc.DisplayName;
                        }
                    }
                }
                catch { }
            }

            return id;
        }

        private void BtnDetails_Click(object sender, EventArgs e)
        {
            ComponentStatusReport report = engine.GetStatusReport();
            string currentLang = lang;
            Dictionary<string, string> dict = Program.LoadLocalization(currentLang);

            string catSvc = LocalizationManager.Get(dict, "CatService", "Service");
            string catReg = LocalizationManager.Get(dict, "CatRegistry", "Registry");
            string catPol = LocalizationManager.Get(dict, "CatPolicy", "Group Policy");
            string catTask = LocalizationManager.Get(dict, "CatTask", "Task");
            string catProc = LocalizationManager.Get(dict, "CatProcess", "Process");
            string catPath = LocalizationManager.Get(dict, "CatImagePath", "Service Path");

            string txtValDisabled = LocalizationManager.Get(dict, "StatusValDisabled", "[DISABLED]");
            string txtValActive = LocalizationManager.Get(dict, "StatusValActive", "[ACTIVE]");
            string txtValLocked = LocalizationManager.Get(dict, "StatusValLocked", "[LOCKED]");
            string txtValOpen = LocalizationManager.Get(dict, "StatusValOpen", "[OPEN]");
            string txtValEnforced = LocalizationManager.Get(dict, "StatusValEnforced", "[ENFORCED]");
            string txtValCleared = LocalizationManager.Get(dict, "StatusValCleared", "[CLEARED]");
            string txtValDeny = LocalizationManager.Get(dict, "StatusValDeny", "[DENY EXECUTE]");
            string txtValAllow = LocalizationManager.Get(dict, "StatusValAllow", "[ALLOWED]");
            string txtValNull = LocalizationManager.Get(dict, "StatusValNull", "[CLEARED / NULL]");
            string txtValOriginal = LocalizationManager.Get(dict, "StatusValOriginal", "[ORIGINAL]");
            string txtValYes = LocalizationManager.Get(dict, "StatusValYes", "Yes");
            string txtValNo = LocalizationManager.Get(dict, "StatusValNo", "No");

            List<DiagnosticItem> items = new List<DiagnosticItem>();

            // 1. Services
            foreach (KeyValuePair<string, bool> kv in report.Services)
            {
                DiagnosticItem itm = new DiagnosticItem();
                itm.CategoryKey = "svc";
                itm.CategoryName = catSvc;
                itm.Id = kv.Key;
                itm.Description = GetDiagnosticItemDescription(dict, "svc", kv.Key);
                itm.IsBlocked = kv.Value;
                itm.StatusText = kv.Value ? txtValDisabled : txtValActive;
                items.Add(itm);
            }

            // 2. Registry Write Locks
            foreach (KeyValuePair<string, bool> kv in report.RegistryKeys)
            {
                DiagnosticItem itm = new DiagnosticItem();
                itm.CategoryKey = "reg";
                itm.CategoryName = catReg;
                itm.Id = kv.Key;
                itm.Description = GetDiagnosticItemDescription(dict, "svc", kv.Key) + " (DACL)";
                itm.IsBlocked = kv.Value;
                itm.StatusText = kv.Value ? txtValLocked : txtValOpen;
                items.Add(itm);
            }

            // 3. Registry Policies
            foreach (KeyValuePair<string, bool> kv in report.Policies)
            {
                DiagnosticItem itm = new DiagnosticItem();
                itm.CategoryKey = "pol";
                itm.CategoryName = catPol;
                itm.Id = kv.Key;
                itm.Description = GetDiagnosticItemDescription(dict, "pol", kv.Key);
                itm.IsBlocked = kv.Value;
                itm.StatusText = kv.Value ? txtValEnforced : txtValCleared;
                items.Add(itm);
            }

            // 4. Scheduled Tasks
            foreach (KeyValuePair<string, bool> kv in report.Tasks)
            {
                DiagnosticItem itm = new DiagnosticItem();
                itm.CategoryKey = "task";
                itm.CategoryName = catTask;
                itm.Id = kv.Key;
                itm.Description = GetDiagnosticItemDescription(dict, "task", kv.Key);
                itm.IsBlocked = kv.Value;
                itm.StatusText = kv.Value ? txtValLocked : txtValActive;
                items.Add(itm);
            }

            // 5. Process Restrictions
            foreach (KeyValuePair<string, bool> kv in report.Processes)
            {
                DiagnosticItem itm = new DiagnosticItem();
                itm.CategoryKey = "proc";
                itm.CategoryName = catProc;
                itm.Id = kv.Key;
                itm.Description = GetDiagnosticItemDescription(dict, "proc", kv.Key);
                itm.IsBlocked = kv.Value;
                itm.StatusText = kv.Value ? txtValDeny : txtValAllow;
                items.Add(itm);
            }

            // 6. Image Paths
            DiagnosticItem itmPath = new DiagnosticItem();
            itmPath.CategoryKey = "path";
            itmPath.CategoryName = catPath;
            itmPath.Id = "ImagePathServices";
            itmPath.Description = string.Join(", ", engine.Config.ImagePathServices);
            itmPath.IsBlocked = report.ImagePathsCleared;
            itmPath.StatusText = report.ImagePathsCleared ? txtValNull : txtValOriginal;
            items.Add(itmPath);

            // Generate Raw Text Report with Dynamic Column Widths per Category
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("==========================================================================================");
            sb.AppendLine("     " + LocalizationManager.Get(dict, "DiagHeader", "WU LOCKER STATUS DIAGNOSTICS"));
            sb.AppendLine("==========================================================================================");
            sb.AppendLine(string.Format("{0,-32} : {1}", LocalizationManager.Get(dict, "DiagOverall", "Overall Status"), report.OverallBlocked ? txtValLocked : txtValActive));
            sb.AppendLine(string.Format("{0,-32} : {1}", LocalizationManager.Get(dict, "DiagStoreMode", "Allow Microsoft Store Mode"), engine.Config.AllowMicrosoftStore ? txtValYes : txtValNo));
            sb.AppendLine();

            string[] catKeys = new string[] { "svc", "reg", "pol", "task", "proc", "path" };
            foreach (string ck in catKeys)
            {
                List<DiagnosticItem> catGroup = new List<DiagnosticItem>();
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].CategoryKey == ck) catGroup.Add(items[i]);
                }
                if (catGroup.Count == 0) continue;

                string groupHeader = catGroup[0].CategoryName.ToUpper();
                sb.AppendLine(string.Format("--- {0} ---", groupHeader));

                int maxIdLen = 0;
                int maxDescLen = 0;
                for (int i = 0; i < catGroup.Count; i++)
                {
                    if (catGroup[i].Id.Length > maxIdLen) maxIdLen = catGroup[i].Id.Length;
                    if (catGroup[i].Description.Length > maxDescLen) maxDescLen = catGroup[i].Description.Length;
                }
                if (maxIdLen < 12) maxIdLen = 12;
                if (maxDescLen < 24) maxDescLen = 24;

                for (int i = 0; i < catGroup.Count; i++)
                {
                    sb.AppendLine(string.Format("  {0} | {1} : {2}",
                        catGroup[i].Id.PadRight(maxIdLen),
                        catGroup[i].Description.PadRight(maxDescLen),
                        catGroup[i].StatusText));
                }
                sb.AppendLine();
            }
            string rawReportText = sb.ToString();

            // Build Modal Window with Strict TableLayoutPanel Hierarchy (Prevents any overlap or clipping)
            using (Form modal = new Form())
            {
                modal.Text = txtDetailsTitle;
                modal.StartPosition = FormStartPosition.CenterParent;
                modal.ClientSize = new Size(ScaleValue(860), ScaleValue(620));
                modal.MinimumSize = new Size(ScaleValue(700), ScaleValue(480));
                modal.BackColor = ColorBg;
                modal.ForeColor = ColorText;
                modal.FormBorderStyle = FormBorderStyle.Sizable;
                modal.MaximizeBox = true;
                modal.MinimizeBox = false;
                modal.ShowInTaskbar = false;
                modal.Font = new Font("Segoe UI", 9F);
                modal.Icon = this.Icon;
                modal.KeyPreview = true;
                modal.KeyDown += delegate(object s, KeyEventArgs ev)
                {
                    if (ev.KeyCode == Keys.Escape) modal.Close();
                };

                // Root TableLayoutPanel (1 Column, 4 Rows) - Eliminates WinForms Dock Z-Order bugs completely
                TableLayoutPanel rootLayout = new TableLayoutPanel();
                rootLayout.Dock = DockStyle.Fill;
                rootLayout.ColumnCount = 1;
                rootLayout.RowCount = 4;
                rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Row 0: Header Banner
                rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Row 1: Filter / Toolbar
                rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));      // Row 2: Center (Grid / Raw Text)
                rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Row 3: Footer
                rootLayout.Margin = new Padding(0);
                rootLayout.Padding = new Padding(0);
                modal.Controls.Add(rootLayout);

                // ================= ROW 0: TOP SUMMARY BANNER =================
                Panel topPanel = new Panel();
                topPanel.Dock = DockStyle.Fill;
                topPanel.AutoSize = true;
                topPanel.BackColor = Color.FromArgb(12, 19, 36);
                topPanel.Margin = new Padding(0);
                topPanel.Padding = new Padding(ScaleValue(14), ScaleValue(10), ScaleValue(14), ScaleValue(10));

                TableLayoutPanel topLayout = new TableLayoutPanel();
                topLayout.Dock = DockStyle.Fill;
                topLayout.AutoSize = true;
                topLayout.ColumnCount = 2;
                topLayout.RowCount = 1;
                topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                topPanel.Controls.Add(topLayout);

                FlowLayoutPanel titleFlow = new FlowLayoutPanel();
                titleFlow.Dock = DockStyle.Fill;
                titleFlow.AutoSize = true;
                titleFlow.FlowDirection = FlowDirection.TopDown;
                titleFlow.WrapContents = false;
                titleFlow.Margin = new Padding(0);

                Label lblHeader = new Label();
                lblHeader.Text = LocalizationManager.Get(dict, "DiagHeader", "WU LOCKER STATUS DIAGNOSTICS");
                lblHeader.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
                lblHeader.ForeColor = ColorText;
                lblHeader.AutoSize = true;
                lblHeader.Margin = new Padding(0, 0, 0, ScaleValue(4));
                titleFlow.Controls.Add(lblHeader);

                FlowLayoutPanel badgesFlow = new FlowLayoutPanel();
                badgesFlow.AutoSize = true;
                badgesFlow.FlowDirection = FlowDirection.LeftToRight;
                badgesFlow.Margin = new Padding(0);

                // Overall Status Pill
                Label pillStatus = new Label();
                pillStatus.Text = report.OverallBlocked ? txtValLocked : txtValActive;
                pillStatus.BackColor = report.OverallBlocked ? Color.FromArgb(16, 185, 129) : Color.FromArgb(59, 130, 246);
                pillStatus.ForeColor = Color.White;
                pillStatus.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
                pillStatus.AutoSize = true;
                pillStatus.Padding = new Padding(ScaleValue(8), ScaleValue(3), ScaleValue(8), ScaleValue(3));
                pillStatus.Margin = new Padding(0, 0, ScaleValue(8), 0);
                badgesFlow.Controls.Add(pillStatus);

                // Store Mode Pill
                Label pillStore = new Label();
                pillStore.Text = LocalizationManager.Get(dict, "DiagStoreMode", "Store Mode") + ": " + (engine.Config.AllowMicrosoftStore ? txtValYes : txtValNo);
                pillStore.BackColor = Color.FromArgb(30, 41, 59);
                pillStore.ForeColor = Color.FromArgb(147, 197, 253);
                pillStore.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
                pillStore.AutoSize = true;
                pillStore.Padding = new Padding(ScaleValue(8), ScaleValue(3), ScaleValue(8), ScaleValue(3));
                badgesFlow.Controls.Add(pillStore);

                titleFlow.Controls.Add(badgesFlow);
                topLayout.Controls.Add(titleFlow, 0, 0);

                // Action Buttons Flow (Copy + View Toggle)
                FlowLayoutPanel actionsFlow = new FlowLayoutPanel();
                actionsFlow.AutoSize = true;
                actionsFlow.FlowDirection = FlowDirection.LeftToRight;
                actionsFlow.WrapContents = false;
                actionsFlow.Margin = new Padding(0);
                actionsFlow.Anchor = AnchorStyles.Right | AnchorStyles.Top;

                Button btnCopy = new Button();
                btnCopy.Text = LocalizationManager.Get(dict, "DiagBtnCopy", "Copy Report");
                btnCopy.Height = ScaleValue(32);
                btnCopy.AutoSize = true;
                btnCopy.FlatStyle = FlatStyle.Flat;
                btnCopy.BackColor = Color.FromArgb(30, 41, 59);
                btnCopy.ForeColor = ColorText;
                btnCopy.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                btnCopy.Margin = new Padding(0, 0, ScaleValue(8), 0);
                btnCopy.Cursor = Cursors.Hand;
                btnCopy.FlatAppearance.BorderSize = 1;
                btnCopy.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
                btnCopy.Click += delegate(object s, EventArgs ev)
                {
                    try
                    {
                        Clipboard.SetText(rawReportText);
                        string origText = btnCopy.Text;
                        btnCopy.Text = LocalizationManager.Get(dict, "DiagCopiedMsg", "Copied!");
                        Timer t = new Timer();
                        t.Interval = 1800;
                        t.Tick += delegate(object ts, EventArgs tev)
                        {
                            btnCopy.Text = origText;
                            t.Stop();
                            t.Dispose();
                        };
                        t.Start();
                    }
                    catch { }
                };
                actionsFlow.Controls.Add(btnCopy);

                Button btnToggleView = new Button();
                btnToggleView.Text = LocalizationManager.Get(dict, "DiagTabRaw", "Raw Text");
                btnToggleView.Height = ScaleValue(32);
                btnToggleView.AutoSize = true;
                btnToggleView.FlatStyle = FlatStyle.Flat;
                btnToggleView.BackColor = Color.FromArgb(30, 41, 59);
                btnToggleView.ForeColor = ColorText;
                btnToggleView.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                btnToggleView.Margin = new Padding(0);
                btnToggleView.Cursor = Cursors.Hand;
                btnToggleView.FlatAppearance.BorderSize = 1;
                btnToggleView.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
                actionsFlow.Controls.Add(btnToggleView);

                topLayout.Controls.Add(actionsFlow, 1, 0);
                rootLayout.Controls.Add(topPanel, 0, 0);

                // ================= ROW 1: TOOLBAR (SEARCH & CATEGORY FILTERS) =================
                Panel toolPanel = new Panel();
                toolPanel.Dock = DockStyle.Fill;
                toolPanel.AutoSize = true;
                toolPanel.BackColor = Color.FromArgb(18, 26, 44);
                toolPanel.Margin = new Padding(0);
                toolPanel.Padding = new Padding(ScaleValue(14), ScaleValue(6), ScaleValue(14), ScaleValue(6));

                FlowLayoutPanel toolLayout = new FlowLayoutPanel();
                toolLayout.Dock = DockStyle.Fill;
                toolLayout.AutoSize = true;
                toolLayout.FlowDirection = FlowDirection.LeftToRight;
                toolLayout.WrapContents = true;
                toolPanel.Controls.Add(toolLayout);

                TextBox txtSearch = new TextBox();
                txtSearch.Width = ScaleValue(200);
                txtSearch.Height = ScaleValue(28);
                txtSearch.BackColor = ColorCard;
                txtSearch.ForeColor = ColorText;
                txtSearch.Font = new Font("Segoe UI", 9F);
                txtSearch.BorderStyle = BorderStyle.FixedSingle;
                txtSearch.Margin = new Padding(0, ScaleValue(2), ScaleValue(10), 0);
                toolLayout.Controls.Add(txtSearch);

                string activeCatFilter = "ALL";
                List<Button> filterButtons = new List<Button>();

                Action<Button, bool> setFilterBtnState = delegate(Button b, bool active)
                {
                    if (active)
                    {
                        b.BackColor = ColorBlue;
                        b.ForeColor = Color.White;
                        b.FlatAppearance.BorderColor = ColorBlue;
                    }
                    else
                    {
                        b.BackColor = ColorCard;
                        b.ForeColor = ColorTextMuted;
                        b.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
                    }
                };

                string[] filterKeys = new string[] { "ALL", "svc", "reg", "pol", "task", "proc" };
                string[] filterLabels = new string[] {
                    LocalizationManager.Get(dict, "DiagFilterAll", "All"),
                    catSvc,
                    catReg,
                    catPol,
                    catTask,
                    catProc
                };

                // Center Content Declarations
                DataGridView grid = new DataGridView();
                TextBox txtRaw = new TextBox();

                // Footer Declaration
                Label lblCount = new Label();
                lblCount.Text = string.Format(LocalizationManager.Get(dict, "DiagTotalCount", "Total {0} components listed"), items.Count);
                lblCount.ForeColor = ColorTextMuted;
                lblCount.Font = new Font("Segoe UI", 9F);
                lblCount.AutoSize = true;
                lblCount.Dock = DockStyle.Left;
                lblCount.TextAlign = ContentAlignment.MiddleLeft;

                Action applyFilter = delegate()
                {
                    string search = txtSearch.Text.Trim();
                    int visibleCount = 0;
                    grid.SuspendLayout();
                    grid.CurrentCell = null; // Prevents CurrencyManager invisible row exception
                    for (int i = 0; i < grid.Rows.Count; i++)
                    {
                        DataGridViewRow r = grid.Rows[i];
                        DiagnosticItem di = r.Tag as DiagnosticItem;
                        if (di == null) continue;

                        bool catMatch = (activeCatFilter == "ALL" || di.CategoryKey == activeCatFilter);
                        bool searchMatch = string.IsNullOrEmpty(search) ||
                            di.Id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            di.Description.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            di.StatusText.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            di.CategoryName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

                        bool isVis = catMatch && searchMatch;
                        r.Visible = isVis;
                        if (isVis) visibleCount++;
                    }
                    grid.ResumeLayout();
                    lblCount.Text = string.Format(LocalizationManager.Get(dict, "DiagTotalCount", "Total {0} components listed"), visibleCount);
                };

                for (int fi = 0; fi < filterKeys.Length; fi++)
                {
                    string fKey = filterKeys[fi];
                    Button btnF = new Button();
                    btnF.Text = filterLabels[fi];
                    btnF.Height = ScaleValue(28);
                    btnF.AutoSize = true;
                    btnF.FlatStyle = FlatStyle.Flat;
                    btnF.FlatAppearance.BorderSize = 1;
                    btnF.Cursor = Cursors.Hand;
                    btnF.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
                    btnF.Margin = new Padding(0, ScaleValue(1), ScaleValue(5), 0);
                    setFilterBtnState(btnF, fKey == "ALL");

                    btnF.Click += delegate(object s, EventArgs ev)
                    {
                        activeCatFilter = fKey;
                        for (int bi = 0; bi < filterButtons.Count; bi++)
                        {
                            setFilterBtnState(filterButtons[bi], filterButtons[bi] == btnF);
                        }
                        applyFilter();
                    };

                    filterButtons.Add(btnF);
                    toolLayout.Controls.Add(btnF);
                }

                txtSearch.TextChanged += delegate(object s, EventArgs ev)
                {
                    applyFilter();
                };

                rootLayout.Controls.Add(toolPanel, 0, 1);

                // ================= ROW 2: CENTER CONTENT PANEL (100% HEIGHT) =================
                Panel centerPanel = new Panel();
                centerPanel.Dock = DockStyle.Fill;
                centerPanel.Margin = new Padding(0);
                centerPanel.Padding = new Padding(0);

                // Monospace Raw Text View (WordWrap = False guarantees lines never break)
                txtRaw.Multiline = true;
                txtRaw.ReadOnly = true;
                txtRaw.WordWrap = false;
                txtRaw.ScrollBars = ScrollBars.Both;
                txtRaw.Dock = DockStyle.Fill;
                txtRaw.BackColor = ColorCard;
                txtRaw.ForeColor = ColorText;
                txtRaw.Font = new Font("Consolas", 9.5F);
                txtRaw.BorderStyle = BorderStyle.None;
                txtRaw.Text = rawReportText;
                txtRaw.Visible = false;
                centerPanel.Controls.Add(txtRaw);

                // DataGridView Setup
                grid.Dock = DockStyle.Fill;
                grid.AllowUserToAddRows = false;
                grid.AllowUserToDeleteRows = false;
                grid.AllowUserToResizeRows = false;
                grid.ReadOnly = true;
                grid.RowHeadersVisible = false;
                grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                grid.MultiSelect = true;
                grid.BackgroundColor = ColorCard;
                grid.BorderStyle = BorderStyle.None;
                grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
                grid.GridColor = Color.FromArgb(40, 53, 76);
                grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersHeight = ScaleValue(34);
                grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
                grid.ColumnHeadersVisible = true;

                // Reflection for double-buffered smooth painting
                try
                {
                    typeof(DataGridView).InvokeMember("DoubleBuffered",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.SetProperty,
                        null, grid, new object[] { true });
                }
                catch { }

                grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(10, 16, 30);
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(226, 232, 240);
                grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
                grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(ScaleValue(4), 0, 0, 0);

                grid.DefaultCellStyle.BackColor = Color.FromArgb(20, 28, 46);
                grid.DefaultCellStyle.ForeColor = ColorText;
                grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(49, 65, 96);
                grid.DefaultCellStyle.SelectionForeColor = Color.White;
                grid.DefaultCellStyle.Font = new Font("Segoe UI", 9F);
                grid.DefaultCellStyle.Padding = new Padding(ScaleValue(4), ScaleValue(2), ScaleValue(4), ScaleValue(2));

                grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(27, 38, 60);

                DataGridViewTextBoxColumn colCat = new DataGridViewTextBoxColumn();
                colCat.HeaderText = LocalizationManager.Get(dict, "DiagColCategory", "Category");
                colCat.Width = ScaleValue(100);
                colCat.SortMode = DataGridViewColumnSortMode.Automatic;

                DataGridViewTextBoxColumn colId = new DataGridViewTextBoxColumn();
                colId.HeaderText = LocalizationManager.Get(dict, "DiagColComponent", "Component (ID)");
                colId.Width = ScaleValue(250);
                colId.SortMode = DataGridViewColumnSortMode.Automatic;
                colId.DefaultCellStyle.Font = new Font("Consolas", 9F, FontStyle.Regular);

                DataGridViewTextBoxColumn colDesc = new DataGridViewTextBoxColumn();
                colDesc.HeaderText = LocalizationManager.Get(dict, "DiagColDescription", "Description");
                colDesc.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                colDesc.SortMode = DataGridViewColumnSortMode.Automatic;

                DataGridViewTextBoxColumn colStatus = new DataGridViewTextBoxColumn();
                colStatus.HeaderText = LocalizationManager.Get(dict, "DiagColStatus", "Status");
                colStatus.Width = ScaleValue(140);
                colStatus.SortMode = DataGridViewColumnSortMode.Automatic;
                colStatus.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

                grid.Columns.Add(colCat);
                grid.Columns.Add(colId);
                grid.Columns.Add(colDesc);
                grid.Columns.Add(colStatus);

                for (int i = 0; i < items.Count; i++)
                {
                    DiagnosticItem di = items[i];
                    int rIdx = grid.Rows.Add(new object[] { di.CategoryName, di.Id, di.Description, di.StatusText });
                    DataGridViewRow row = grid.Rows[rIdx];
                    row.Tag = di;

                    if (di.IsBlocked)
                    {
                        row.Cells[3].Style.ForeColor = Color.FromArgb(52, 211, 153); // emerald-400
                        row.Cells[3].Style.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                    }
                    else
                    {
                        row.Cells[3].Style.ForeColor = Color.FromArgb(96, 165, 250); // blue-400
                        row.Cells[3].Style.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
                    }
                }

                centerPanel.Controls.Add(grid);
                rootLayout.Controls.Add(centerPanel, 0, 2);

                // ================= ROW 3: FOOTER PANEL =================
                Panel bottomPanel = new Panel();
                bottomPanel.Dock = DockStyle.Fill;
                bottomPanel.AutoSize = true;
                bottomPanel.BackColor = Color.FromArgb(12, 19, 36);
                bottomPanel.Margin = new Padding(0);
                bottomPanel.Padding = new Padding(ScaleValue(14), ScaleValue(8), ScaleValue(14), ScaleValue(8));

                TableLayoutPanel bottomLayout = new TableLayoutPanel();
                bottomLayout.Dock = DockStyle.Fill;
                bottomLayout.AutoSize = true;
                bottomLayout.ColumnCount = 2;
                bottomLayout.RowCount = 1;
                bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                bottomPanel.Controls.Add(bottomLayout);

                bottomLayout.Controls.Add(lblCount, 0, 0);

                Button btnCloseModal = new Button();
                btnCloseModal.Text = LocalizationManager.Get(dict, "DiagBtnClose", "OK");
                btnCloseModal.Width = ScaleValue(100);
                btnCloseModal.Height = ScaleValue(32);
                btnCloseModal.FlatStyle = FlatStyle.Flat;
                btnCloseModal.BackColor = ColorBlue;
                btnCloseModal.ForeColor = Color.White;
                btnCloseModal.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                btnCloseModal.Cursor = Cursors.Hand;
                btnCloseModal.Click += delegate(object s, EventArgs ev) { modal.Close(); };
                bottomLayout.Controls.Add(btnCloseModal, 1, 0);

                rootLayout.Controls.Add(bottomPanel, 0, 3);

                // View Toggle Button Handler
                btnToggleView.Click += delegate(object s, EventArgs ev)
                {
                    if (grid.Visible)
                    {
                        grid.Visible = false;
                        txtRaw.Visible = true;
                        toolPanel.Visible = false;
                        btnToggleView.Text = LocalizationManager.Get(dict, "DiagTabGrid", "Structured Table");
                        txtRaw.SelectionStart = 0;
                        txtRaw.SelectionLength = 0;
                    }
                    else
                    {
                        txtRaw.Visible = false;
                        grid.Visible = true;
                        toolPanel.Visible = true;
                        btnToggleView.Text = LocalizationManager.Get(dict, "DiagTabRaw", "Raw Text");
                    }
                };

                modal.ShowDialog(this);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(Color.FromArgb(30, 41, 59), 2))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
            }
        }

        private void ComboLang_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboLang.SelectedItem != null)
            {
                string selectedLang = comboLang.SelectedItem.ToString();
                WriteIni("Settings", "Language", selectedLang);
                ApplyLocalization();
                UpdateStatusGUI();
            }
        }
    }

    public class CustomButton : Button
    {
        private Color hoverBgColor = Color.FromArgb(51, 65, 85);
        private Color normalBgColor = Color.FromArgb(30, 41, 59);

        public CustomButton()
        {
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.Cursor = Cursors.Hand;
            this.AutoEllipsis = true;
            this.normalBgColor = this.BackColor;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            this.normalBgColor = this.BackColor;
            this.BackColor = Color.FromArgb(
                Math.Min(255, this.normalBgColor.R + 25),
                Math.Min(255, this.normalBgColor.G + 25),
                Math.Min(255, this.normalBgColor.B + 25)
            );
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            this.BackColor = this.normalBgColor;
        }
    }
}

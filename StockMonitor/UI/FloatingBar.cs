namespace StockMonitor.UI;

/// <summary>
/// 悬浮窗 — 默认屏幕右侧中间靠边, 可拖动, 可切换前台/后台
/// </summary>
public class FloatingBar : Form
{
    private readonly Label _pinyinLabel;
    private readonly Label _priceLabel;
    private readonly Label _signalLabel;
    private Point _dragStart;
    private bool _dragging;
    private bool _didDrag;
    private ContextMenuStrip? _lastMenu; // 上一次弹出的菜单, 需要释放

    public event EventHandler? NextStockRequested;

    public FloatingBar()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true; // 默认前台
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(245, 245, 240);
        Size = new Size(180, 36);

        // 默认: 屏幕右侧中间靠边
        var screen = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(screen.Right - Width, screen.Top + screen.Height / 2 - Height / 2);

        // 拼音缩写 — 黑色
        _pinyinLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei", 13f, FontStyle.Bold),
            ForeColor = Color.Black,
            Location = new Point(6, 5),
            Text = "----"
        };

        // 股价 — 淡黄色
        _priceLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei", 13f, FontStyle.Bold),
            ForeColor = Color.FromArgb(180, 160, 50),
            Location = new Point(70, 5),
            Text = ""
        };

        // 信号 B/S — B红色 S绿色
        _signalLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei", 13f, FontStyle.Bold),
            ForeColor = Color.Transparent,
            Location = new Point(150, 5),
            Text = ""
        };

        Controls.AddRange(new Control[] { _pinyinLabel, _priceLabel, _signalLabel });

        // 左键: 拖动(>5px) 或 点击切换; 右键: 菜单
        void onDown(object? s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _dragging = false; _dragStart = e.Location; _didDrag = false; } }
        void onMove(object? s, MouseEventArgs e) {
            if (e.Button == MouseButtons.Left) {
                if (!_didDrag && (Math.Abs(e.X - _dragStart.X) > 5 || Math.Abs(e.Y - _dragStart.Y) > 5))
                    _didDrag = true;
                if (_didDrag)
                    Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y);
            }
        }
        void onUp(object? s, MouseEventArgs e) {
            if (e.Button == MouseButtons.Left && !_didDrag)
                NextStockRequested?.Invoke(this, EventArgs.Empty);
            _didDrag = false;
        }
        void onRight(object? s, MouseEventArgs e) { if (e.Button == MouseButtons.Right) ShowCombinedMenu(); }

        foreach (Control c in Controls)
        {
            c.MouseDown += onDown;
            c.MouseMove += onMove;
            c.MouseUp += onUp;
            c.MouseClick += onRight;
        }
        MouseDown += onDown;
        MouseMove += onMove;
        MouseUp += onUp;

    }

    private ContextMenuStrip? _externalMenu;
    private ToolStripMenuItem? _pinItem;

    /// <summary>
    /// 绑定外部右键菜单, 右键时动态组装显示
    /// </summary>
    public void SetExternalMenu(ContextMenuStrip externalMenu)
    {
        _externalMenu = externalMenu;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
            ShowCombinedMenu();
        base.OnMouseClick(e);
    }

    private void ShowCombinedMenu()
    {
        // 释放上一次的菜单
        _lastMenu?.Dispose();

        var menu = new ContextMenuStrip();
        _lastMenu = menu;

        // 隐藏悬浮窗
        var hideItem = new ToolStripMenuItem("隐藏悬浮窗");
        hideItem.Click += (_, _) => Hide();
        menu.Items.Add(hideItem);

        // 固定前台/后台
        var pin = new ToolStripMenuItem(TopMost ? "✓ 固定前台" : "  固定前台");
        pin.Click += (_, _) => { TopMost = !TopMost; };
        menu.Items.Add(pin);

        menu.Items.Add(new ToolStripSeparator());

        // 追加外部菜单项(添加财富/退出等)
        if (_externalMenu != null)
        {
            foreach (ToolStripItem item in _externalMenu.Items)
            {
                if (item is ToolStripMenuItem mi)
                {
                    var clone = new ToolStripMenuItem(mi.Text);
                    var src = mi;
                    clone.Click += (_, _) => src.PerformClick();

                    // 克隆子菜单(如"移除")
                    foreach (ToolStripItem sub in mi.DropDownItems)
                    {
                        if (sub is ToolStripMenuItem subMi)
                        {
                            var subClone = new ToolStripMenuItem(subMi.Text);
                            var subSrc = subMi;
                            subClone.Click += (_, _) => subSrc.PerformClick();
                            clone.DropDownItems.Add(subClone);
                        }
                    }

                    menu.Items.Add(clone);
                }
                else if (item is ToolStripSeparator)
                {
                    menu.Items.Add(new ToolStripSeparator());
                }
            }
        }

        // 靠右时向左弹出，避免菜单跑到屏幕外
        var pos = Cursor.Position;
        var screen = Screen.FromPoint(pos).WorkingArea;
        menu.Show(pos);
        if (menu.Right > screen.Right)
            menu.Left = pos.X - menu.Width;
        if (menu.Bottom > screen.Bottom)
            menu.Top = pos.Y - menu.Height;
    }

    public void ToggleVisibility()
    {
        if (Visible)
        {
            // 已显示: 提到最前面
            TopMost = true;
            BringToFront();
            Activate();
        }
        else
        {
            Show();
            TopMost = true;
            BringToFront();
            Activate();
        }
    }

    public void UpdateDisplay(string pinyin, string name, double price,
        double changePercent, string longSignalType, string shortSignalType)
    {
        _pinyinLabel.Text = pinyin;
        _priceLabel.Text = $"{price:F2}";
        _priceLabel.Location = new Point(_pinyinLabel.Right + 6, 5);

        // 涨停检测: 涨幅>=9.8% 显示红色"---"
        // ST股涨幅>=4.8% 也算涨停, 但这里统一用9.8%
        bool isLimitUp = changePercent >= 9.8;

        // 信号文字优先级: 涨停 > 大周期(红B/绿S) > 短周期(淡黄B/S)
        var sigText = "";
        var sigColor = Color.Transparent;

        if (isLimitUp)
        {
            sigText = "---";
            sigColor = Color.FromArgb(255, 50, 50); // 红色
        }
        else if (longSignalType == "Buy")
        {
            sigText = "B";
            sigColor = Color.FromArgb(255, 50, 50); // 红色
        }
        else if (longSignalType == "Sell")
        {
            sigText = "S";
            sigColor = Color.FromArgb(50, 220, 50); // 绿色
        }
        else if (shortSignalType == "Buy")
        {
            sigText = "B";
            sigColor = Color.FromArgb(180, 160, 50); // 淡黄色
        }
        else if (shortSignalType == "Sell")
        {
            sigText = "S";
            sigColor = Color.FromArgb(180, 160, 50); // 淡黄色
        }

        _signalLabel.Text = sigText;
        _signalLabel.ForeColor = sigColor;
        _signalLabel.Location = new Point(_priceLabel.Right + 4, 5);

        var rightEdge = sigText.Length > 0 ? _signalLabel.Right : _priceLabel.Right;
        Width = rightEdge + 10;
    }

    public void UpdateNoStock()
    {
        _pinyinLabel.Text = "右键";
        _priceLabel.Text = "添加财富";
        _priceLabel.Location = new Point(_pinyinLabel.Right + 6, 5);
        _signalLabel.Text = "";
        Width = _priceLabel.Right + 10;
    }

    public void UpdateInitializing(int ready, int total)
    {
        _pinyinLabel.Text = "初始化";
        _priceLabel.Text = $"({ready}/{total})";
        _priceLabel.Location = new Point(_pinyinLabel.Right + 6, 5);
        _signalLabel.Text = "";
    }

    public void SetSignalType(string type) { }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW: 不在Alt+Tab显示
            return cp;
        }
    }
}

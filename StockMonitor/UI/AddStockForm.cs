using StockMonitor.Api;

namespace StockMonitor.UI;

public class AddStockForm : Form
{
    private readonly TextBox _searchBox;
    private readonly ListBox _resultList;
    private readonly Button _okButton;
    private readonly Button _cancelButton;
    private readonly System.Windows.Forms.Timer _debounceTimer;
    private readonly CancellationTokenSource _cts = new();

    private List<SearchResult> _results = new();
    public SearchResult? SelectedStock { get; private set; }

    public AddStockForm()
    {
        Text = "添加财富";
        Size = new Size(360, 340);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var label = new Label
        {
            Text = "输入拼音首字母/代码/名称:",
            Location = new Point(12, 12),
            AutoSize = true
        };

        _searchBox = new TextBox
        {
            Location = new Point(12, 35),
            Size = new Size(320, 25)
        };
        _searchBox.TextChanged += OnSearchTextChanged;

        _resultList = new ListBox
        {
            Location = new Point(12, 65),
            Size = new Size(320, 180)
        };
        _resultList.DoubleClick += (_, _) => OnOkClick(null, EventArgs.Empty);

        _okButton = new Button
        {
            Text = "确定",
            Location = new Point(170, 255),
            Size = new Size(75, 28),
            DialogResult = DialogResult.OK
        };
        _okButton.Click += OnOkClick;

        _cancelButton = new Button
        {
            Text = "取消",
            Location = new Point(257, 255),
            Size = new Size(75, 28),
            DialogResult = DialogResult.Cancel
        };

        // 300ms防抖定时器
        _debounceTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _debounceTimer.Tick += OnDebounceSearch;

        Controls.AddRange(new Control[] { label, _searchBox, _resultList, _okButton, _cancelButton });
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
    }

    private void OnSearchTextChanged(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private async void OnDebounceSearch(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        var keyword = _searchBox.Text.Trim();
        if (keyword.Length < 1) { _resultList.Items.Clear(); return; }

        _results = await EastMoneyClient.Search(keyword, _cts.Token);
        _resultList.Items.Clear();
        foreach (var r in _results)
        {
            _resultList.Items.Add($"{r.Name} ({r.Code})  {r.PinYin}");
        }
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        if (_resultList.SelectedIndex >= 0 && _resultList.SelectedIndex < _results.Count)
        {
            SelectedStock = _results[_resultList.SelectedIndex];
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            _debounceTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}

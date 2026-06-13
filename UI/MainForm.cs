using System.Text;
using System.Text.Json;
using SongConverter.Core;
using SongConverter.Utils;

namespace SongConverter.UI;

public partial class MainForm : Form
{
    private string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SongConverter", "settings.json");
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private (string InternalName, string DisplayName, string FetchFileName)[] GetCategoryMap() => new[]
    {
        ("00 ポップス", LanguageManager.GetString("CatPops"), "pops.php"),
        ("01 キッズ", LanguageManager.GetString("CatKids"), "kids.php"),
        ("02 アニメ", LanguageManager.GetString("CatAnime"), "anime.php"),
        ("03 ボーカロイド™曲", LanguageManager.GetString("CatVocaloid"), "vocaloid.php"),
        ("04 ゲームミュージック", LanguageManager.GetString("CatGame"), "game.php"),
        ("05 バラエティ", LanguageManager.GetString("CatVariety"), "variety.php"),
        ("06 クラシック", LanguageManager.GetString("CatClassic"), "classic.php"),
        ("07 ナムコオリジナル", LanguageManager.GetString("CatNamco"), "namco.php")
    };

    private readonly HashSet<string> _selectedSourceCategories = new(SongSorterCore.SourceCategories, StringComparer.OrdinalIgnoreCase);
    private Button? _btnCategorySelect;
    private Button? _btnImageSelect;
    private Button? _btnConvertAssetSelect;
    // 共通画像設定
    private readonly Core.DanGeneratorCore.DanImageSettings _commonImageSettings = new();
    // 段位ごとの画像設定
    private readonly Dictionary<string, Core.DanGeneratorCore.DanImageSettings> _danImageSettings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _convertAssetAssignments = new(StringComparer.OrdinalIgnoreCase);
    private ToolStripStatusLabel? _cancelStatusLink;
    private CancellationTokenSource? _operationCts;

    public MainForm()
    {
        InitializeComponent();
        if (File.Exists("SongConverter.ico"))
        {
            this.Icon = new Icon("SongConverter.ico");
        }
        LoadSettings();
        ApplyLocalization();
        logBox.Text = LanguageManager.GetString("Ready") + Environment.NewLine;
        
        // 起動時にアップデートチェックを非同期で実行
        this.Load += async (s, e) => await CheckForUpdateOnStartupAsync();
        
        // Browsing
        btnBrowseAddSongsFolder.Click += (s, e) => BrowseFolder(txtAddSongsFolder);
        btnBrowseTemp.Click += (s, e) => BrowseFolder(txtTempSongs);
        btnBrowseRoot.Click += (s, e) => BrowseFolder(txtTaikoRoot);
        btnBrowseDanSongs.Click += (s, e) => BrowseFolder(txtDanSongsPath);
        btnBrowseDanOutputFolder.Click += (s, e) => BrowseFolder(txtDanOutputFolder);
        btnBrowseDanConvertSimu.Click += (s, e) => BrowseFolder(txtDanConvertSimu);
        btnBrowseDanConvertOutputFolder.Click += (s, e) => BrowseFolder(txtDanConvertOutputFolder);
        btnBrowseTjaFile.Click += (s, e) => BrowseFile(txtTjaFile, "TJA files (*.tja)|*.tja|All files (*.*)|*.*");

        // Save Settings on text change
        txtAddSongsFolder.TextChanged += (s, e) => SaveSettings();
        txtTempSongs.TextChanged += (s, e) => SaveSettings();
        txtDanOutputFolder.TextChanged += (s, e) => SaveSettings();
        txtDanConvertOutputFolder.TextChanged += (s, e) => SaveSettings();
        txtWikiUrl.TextChanged += (s, e) =>
            {
                _commonImageSettings.PlatePath = null;
                _commonImageSettings.PanelSidePath = null;
                _commonImageSettings.TitlePlatePath = null;
                _commonImageSettings.MiniPlatePath = null;
                _danImageSettings.Clear();
                SaveSettings();
            };
        
        // Operations
        btnOrganize.Click += async (s, e) => await OnOrganizeClick();
        btnGenerateDan.Click += async (s, e) => await OnGenerateDanClick();
        btnExecuteAddSongs.Click += async (s, e) => await OnExecuteAddSongsClick();
        btnConvertDan.Click += async (s, e) => await OnConvertDanClick();

        this.Shown += async (s, e) => await OnFetchListsClick(true);

        menuJapanese.Click += (s, e) => ChangeLanguage(Language.Japanese);
        menuEnglish.Click += (s, e) => ChangeLanguage(Language.English);
        statusJapaneseMenu.Click += (s, e) => ChangeLanguage(Language.Japanese);
        statusEnglishMenu.Click += (s, e) => ChangeLanguage(Language.English);

        // D&D
        tabDanConvertor.DragEnter += Control_DragEnter;
        tabDanConvertor.DragDrop += Control_DragDrop;
        txtTjaFile.DragEnter += Control_DragEnter;
        txtTjaFile.DragDrop += Control_DragDrop;

        InitializeCategorySelectorUi();
        InitializePlateSelectorUi();
        InitializeConvertAssetSelectorUi();
        InitializeCancelUi();
        UpdateCategoryButtonText();
    }

    private void Control_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effect = DragDropEffects.Copy;
        else
            e.Effect = DragDropEffects.None;
    }

    private void Control_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data == null) return;
        string[]? files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;

        // If it's the DanConvertor tab or txtTjaFile
        txtTjaFile.Text = string.Join(";", files);
    }

    private void BrowseFile(TextBox target, string filter)
    {
        using var ofd = new OpenFileDialog { Filter = filter };
        if (ofd.ShowDialog() == DialogResult.OK)
        {
            target.Text = ofd.FileName;
            SaveSettings();
        }
    }

    private void BrowseFolder(TextBox target)
    {
        using var fbd = new FolderBrowserDialog();
        if (fbd.ShowDialog() == DialogResult.OK)
        {
            target.Text = fbd.SelectedPath;
            SaveSettings();
        }
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<string>(Log), message);
            return;
        }
        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void SetStatus(string msg, bool showProgress = false)
    {
        if (this.InvokeRequired)
        {
            this.Invoke(new Action(() => SetStatus(msg, showProgress)));
            return;
        }
        statusLabel.Text = msg;
        progressBar.Visible = showProgress;
    }

    private void SetActionButtonsEnabled(bool enabled)
    {
        btnOrganize.Enabled = enabled;
        btnGenerateDan.Enabled = enabled;
        btnExecuteAddSongs.Enabled = enabled;
        btnConvertDan.Enabled = enabled;
    }

    private void InitializeCategorySelectorUi()
    {
        _btnCategorySelect = new Button
        {
            Name = "btnCategorySelect",
            Text = LanguageManager.GetString("CategoryAll"),
            Location = new Point(btnOrganize.Right + 10, btnOrganize.Top),
            Size = btnOrganize.Size,
            BackColor = Color.FromArgb(80, 80, 80),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = btnOrganize.Font,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        _btnCategorySelect.Click += (s, e) =>
        {
            if (ShowCategoryDialog())
            {
                UpdateCategoryButtonText();
                SaveSettings();
            }
        };
        tabSongSorter.Controls.Add(_btnCategorySelect);
    }

    private void InitializePlateSelectorUi()
    {
        _btnImageSelect = new Button
        {
            Name = "btnImageSelect",
            Text = "画像選択",
            Size = btnGenerateDan.Size,
            Location = new Point(btnGenerateDan.Right + 10, btnGenerateDan.Top),
            BackColor = Color.FromArgb(80, 80, 80),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = btnGenerateDan.Font
        };
        _btnImageSelect.Click += async (s, e) => await ShowImageSelectionDialog();
        tabDanGenerator.Controls.Add(_btnImageSelect);
    }

    private async Task ShowImageSelectionDialog()
    {
        if (string.IsNullOrWhiteSpace(txtWikiUrl.Text))
        {
            MessageBox.Show(LanguageManager.GetString("WikiUrlFirst"), LanguageManager.GetString("Ready"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetIndeterminateProgress(true);
        SetActionButtonsEnabled(false);
        try
        {
            var rankGroups = await DanGeneratorCore.FetchRankNamesByVersionAsync(txtWikiUrl.Text);
            if (rankGroups.Count == 0)
            {
                MessageBox.Show(LanguageManager.GetString("DanNameNotFound"), LanguageManager.GetString("Warn"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var dialog = new Form
            {
                Text = "画像設定",
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(800, 650),
                FormBorderStyle = FormBorderStyle.Sizable,
                MinimizeBox = false
            };

            var treeView = new TreeView
            {
                Dock = DockStyle.Fill,
                ShowLines = true,
                ShowPlusMinus = true,
                ShowRootLines = true,
                Font = new Font("Noto Sans", 9F),
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White
            };

            // 共通画像設定ノード
            var commonNode = new TreeNode("共通画像設定") { Tag = "common" };
            treeView.Nodes.Add(commonNode);

            // 全体設定ノード
            var allSettingsNode = new TreeNode("全体設定") { Tag = "all" };
            treeView.Nodes.Add(allSettingsNode);

            // 全ての段位を全体設定の下に直接追加（重複排除して正しい順番でソート）
            var rankNames = new[] { "達人", "超人", "名人", "玄人", "十段", "九段", "八段", "七段", "六段", "五段", "四段", "三段", "二段", "初段", "一級", "二級", "三級", "四級", "五級" };
            var allRanks = new HashSet<string>();
            foreach (var group in rankGroups)
            {
                foreach (var rank in group.Ranks)
                {
                    allRanks.Add(rank);
                }
            }
            // rankNames の順番に従ってソート
            var sortedRanks = allRanks.OrderBy(r => 
            {
                var index = Array.FindIndex(rankNames, rn => r.Contains(rn));
                return index >= 0 ? index : int.MaxValue;
            }).ThenBy(r => r);
            foreach (var rank in sortedRanks)
            {
                var rankNode = new TreeNode(rank) { Tag = new ImageNodeData { Type = "plate", RankKey = rank, DisplayName = rank } };
                allSettingsNode.Nodes.Add(rankNode);
            }

            // 詳細パネル
            var detailPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 400,
                BackColor = Color.FromArgb(50, 50, 50)
            };

            var selectedLabel = new Label
            {
                Text = "項目を選択してください",
                Dock = DockStyle.Top,
                Padding = new Padding(15),
                Font = new Font("Noto Sans", 10F, FontStyle.Bold),
                ForeColor = Color.White
            };
            detailPanel.Controls.Add(selectedLabel);

            // 画像一覧表示用のパネル（共通・段位両方で使用）
            var imageListPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(15),
                Visible = false
            };
            detailPanel.Controls.Add(imageListPanel);

            // 共通画像の一覧を作成するための情報
            var commonAssetsList = new[]
            {
                ("danPlatePath", "Plate"),
                ("danPanelSidePath", "PanelSide"),
                ("danTitlePlatePath", "TitlePlate"),
                ("danMiniPlatePath", "MiniPlate")
            };

            // 現在選択されている段位（nullならnull、共通なら"common
            string? selectedKey = null;
            Dictionary<string, TextBox> textBoxes = new Dictionary<string, TextBox>();

            // ツリー選択イベント
            treeView.AfterSelect += (s, e) =>
            {
                // パネルをクリア
                imageListPanel.Controls.Clear();
                textBoxes.Clear();
                selectedKey = null;

                if (e.Node?.Tag is string tag && tag == "common")
                {
                    // 共通画像設定ノードが選択された場合
                    selectedLabel.Text = "共通画像設定";
                    selectedKey = "common";

                    // 共通画像の一覧を作成
                    foreach (var (key, name) in commonAssetsList)
                    {
                        var row = new Panel { Width = 360, Height = 45 };
                        
                        var lbl = new Label 
                        { 
                            Text = name, 
                            Location = new Point(0, 10), 
                            Width = 100, 
                            Font = new Font("Noto Sans", 9F),
                            ForeColor = Color.White 
                        };
                        
                        // 現在の値を取得
                        string? currentValue = key switch
                        {
                            "danPlatePath" => _commonImageSettings.PlatePath,
                            "danPanelSidePath" => _commonImageSettings.PanelSidePath,
                            "danTitlePlatePath" => _commonImageSettings.TitlePlatePath,
                            "danMiniPlatePath" => _commonImageSettings.MiniPlatePath,
                            _ => null
                        };

                        var txt = new TextBox 
                        { 
                            Text = currentValue ?? "", 
                            Location = new Point(105, 7), 
                            Width = 120,
                            ReadOnly = true,
                            BackColor = Color.FromArgb(60, 60, 60),
                            ForeColor = Color.White
                        };
                        
                        var btnBrowse = new Button 
                        { 
                            Text = "参照", 
                            Location = new Point(230, 4), 
                            Width = 55, 
                            Height = 25,
                            BackColor = Color.FromArgb(0, 122, 204),
                            ForeColor = Color.White,
                            FlatStyle = FlatStyle.Flat
                        };
                        
                        var btnClear = new Button
                        {
                            Text = "クリア",
                            Location = new Point(290, 4),
                            Width = 55,
                            Height = 25,
                            BackColor = Color.FromArgb(200, 50, 50),
                            ForeColor = Color.White,
                            FlatStyle = FlatStyle.Flat
                        };
                        
                        var keyCapture = key;
                        btnBrowse.Click += (s, e) =>
                        {
                            using var ofd = new OpenFileDialog
                            {
                                Filter = "画像ファイル (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|すべてのファイル (*.*)|*.*",
                                Title = "画像を選択"
                            };
                            if (ofd.ShowDialog(dialog) == DialogResult.OK)
                            {
                                txt.Text = ofd.FileName;
                                switch (keyCapture)
                                {
                                    case "danPlatePath":
                                        _commonImageSettings.PlatePath = ofd.FileName;
                                        break;
                                    case "danPanelSidePath":
                                        _commonImageSettings.PanelSidePath = ofd.FileName;
                                        break;
                                    case "danTitlePlatePath":
                                        _commonImageSettings.TitlePlatePath = ofd.FileName;
                                        break;
                                    case "danMiniPlatePath":
                                        _commonImageSettings.MiniPlatePath = ofd.FileName;
                                        break;
                                }
                            }
                        };
                        
                        btnClear.Click += (s, e) =>
                        {
                            txt.Text = "";
                            switch (keyCapture)
                            {
                                case "danPlatePath":
                                    _commonImageSettings.PlatePath = null;
                                    break;
                                case "danPanelSidePath":
                                    _commonImageSettings.PanelSidePath = null;
                                    break;
                                case "danTitlePlatePath":
                                    _commonImageSettings.TitlePlatePath = null;
                                    break;
                                case "danMiniPlatePath":
                                    _commonImageSettings.MiniPlatePath = null;
                                    break;
                            }
                        };
                        
                        row.Controls.Add(lbl);
                        row.Controls.Add(txt);
                        row.Controls.Add(btnBrowse);
                        row.Controls.Add(btnClear);
                        imageListPanel.Controls.Add(row);
                        textBoxes[key] = txt;
                    }

                    imageListPanel.Visible = true;
                    imageListPanel.BringToFront();
                }
                else if (e.Node?.Tag is ImageNodeData data && data.Type == "plate")
                {
                    // 段位ノードが選択された場合
                    selectedLabel.Text = data.DisplayName;
                    selectedKey = data.RankKey;

                    // 段位の設定を取得（なければ作成）
                    if (!_danImageSettings.TryGetValue(data.RankKey, out var danSettings))
                    {
                        danSettings = new Core.DanGeneratorCore.DanImageSettings();
                        _danImageSettings[data.RankKey] = danSettings;
                    }

                    // 段位用の一覧を作成（Plate + 共通画像）
                    var rankAssets = new[]
                    {
                        ("plate", "Plate"),
                        ("danPanelSidePath", "PanelSide"),
                        ("danTitlePlatePath", "TitlePlate"),
                        ("danMiniPlatePath", "MiniPlate")
                    };

                    foreach (var (key, name) in rankAssets)
                    {
                        var row = new Panel { Width = 360, Height = 45 };
                        
                        var lbl = new Label 
                        { 
                            Text = name, 
                            Location = new Point(0, 10), 
                            Width = 100, 
                            Font = new Font("Noto Sans", 9F),
                            ForeColor = Color.White 
                        };
                        
                        // 現在の値を取得
                        string? currentValue = key switch
                        {
                            "plate" => danSettings.PlatePath,
                            "danPanelSidePath" => danSettings.PanelSidePath,
                            "danTitlePlatePath" => danSettings.TitlePlatePath,
                            "danMiniPlatePath" => danSettings.MiniPlatePath,
                            _ => null
                        };

                        var txt = new TextBox 
                        { 
                            Text = currentValue ?? "", 
                            Location = new Point(105, 7), 
                            Width = 120,
                            ReadOnly = true,
                            BackColor = Color.FromArgb(60, 60, 60),
                            ForeColor = Color.White
                        };
                        
                        var btnBrowse = new Button 
                        { 
                            Text = "参照", 
                            Location = new Point(230, 4), 
                            Width = 55, 
                            Height = 25,
                            BackColor = Color.FromArgb(0, 122, 204),
                            ForeColor = Color.White,
                            FlatStyle = FlatStyle.Flat
                        };
                        
                        var btnClear = new Button
                        {
                            Text = "クリア",
                            Location = new Point(290, 4),
                            Width = 55,
                            Height = 25,
                            BackColor = Color.FromArgb(200, 50, 50),
                            ForeColor = Color.White,
                            FlatStyle = FlatStyle.Flat
                        };
                        
                        var keyCapture = key;
                        var rankKeyCapture = data.RankKey;
                        btnBrowse.Click += (s, e) =>
                        {
                            using var ofd = new OpenFileDialog
                            {
                                Filter = "画像ファイル (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|すべてのファイル (*.*)|*.*",
                                Title = "画像を選択"
                            };
                            if (ofd.ShowDialog(dialog) == DialogResult.OK)
                            {
                                txt.Text = ofd.FileName;
                                // 最新の設定を取得
                                if (!_danImageSettings.TryGetValue(rankKeyCapture, out var currentSettings))
                                {
                                    currentSettings = new Core.DanGeneratorCore.DanImageSettings();
                                    _danImageSettings[rankKeyCapture] = currentSettings;
                                }
                                switch (keyCapture)
                                {
                                    case "plate":
                                        currentSettings.PlatePath = ofd.FileName;
                                        break;
                                    case "danPanelSidePath":
                                        currentSettings.PanelSidePath = ofd.FileName;
                                        break;
                                    case "danTitlePlatePath":
                                        currentSettings.TitlePlatePath = ofd.FileName;
                                        break;
                                    case "danMiniPlatePath":
                                        currentSettings.MiniPlatePath = ofd.FileName;
                                        break;
                                }
                            }
                        };
                        
                        btnClear.Click += (s, e) =>
                        {
                            txt.Text = "";
                            // 最新の設定を取得
                            if (!_danImageSettings.TryGetValue(rankKeyCapture, out var currentSettings))
                            {
                                currentSettings = new Core.DanGeneratorCore.DanImageSettings();
                                _danImageSettings[rankKeyCapture] = currentSettings;
                            }
                            switch (keyCapture)
                            {
                                case "plate":
                                    currentSettings.PlatePath = null;
                                    break;
                                case "danPanelSidePath":
                                    currentSettings.PanelSidePath = null;
                                    break;
                                case "danTitlePlatePath":
                                    currentSettings.TitlePlatePath = null;
                                    break;
                                case "danMiniPlatePath":
                                    currentSettings.MiniPlatePath = null;
                                    break;
                            }
                        };
                        
                        row.Controls.Add(lbl);
                        row.Controls.Add(txt);
                        row.Controls.Add(btnBrowse);
                        row.Controls.Add(btnClear);
                        imageListPanel.Controls.Add(row);
                        textBoxes[key] = txt;
                    }

                    imageListPanel.Visible = true;
                    imageListPanel.BringToFront();
                }
                else
                {
                    // その他（バージョンノードなど）
                    selectedLabel.Text = "項目を選択してください";
                    imageListPanel.Visible = false;
                }
            };

            // 全ノード展開
            treeView.ExpandAll();

            // レイアウト
            var btnOk = new Button { Text = "保存", Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.OK };
            btnOk.Font = new Font("Noto Sans", 10F, FontStyle.Bold);
            btnOk.BackColor = Color.FromArgb(0, 153, 255);
            btnOk.ForeColor = Color.White;
            btnOk.FlatStyle = FlatStyle.Flat;

            dialog.Controls.Add(treeView);
            dialog.Controls.Add(detailPanel);
            dialog.Controls.Add(btnOk);

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                SaveSettings();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"解析エラー: {ex.Message}");
        }
        finally
        {
            SetIndeterminateProgress(false);
            SetActionButtonsEnabled(true);
        }
    }

    private class ImageNodeData
    {
        public string Type { get; set; } = ""; // "common" または "plate"
        public string Key { get; set; } = ""; // common の場合のキー
        public string RankKey { get; set; } = ""; // plate の場合のキー
        public string DisplayName { get; set; } = "";
    }



    private void InitializeConvertAssetSelectorUi()
    {
        _btnConvertAssetSelect = new Button
        {
            Name = "btnConvertAssetSelect",
            Text = LanguageManager.GetString("SelectImage"),
            Size = btnConvertDan.Size,
            Location = new Point(btnConvertDan.Right + 10, btnConvertDan.Top),
            BackColor = Color.FromArgb(80, 80, 80),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = btnConvertDan.Font
        };
        _btnConvertAssetSelect.Click += (s, e) => ShowConvertAssetSelectionDialog();
        tabDanConvertor.Controls.Add(_btnConvertAssetSelect);
    }

    private void ShowConvertAssetSelectionDialog()
    {
        using var dialog = new Form
        {
            Text = LanguageManager.GetString("ImageSettingsConv"),
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(800, 650),
            FormBorderStyle = FormBorderStyle.Sizable,
            MinimizeBox = false
        };

        var treeView = new TreeView
        {
            Dock = DockStyle.Fill,
            ShowLines = true,
            ShowPlusMinus = true,
            ShowRootLines = true,
            Font = new Font("Noto Sans", 9F),
            BackColor = Color.FromArgb(40, 40, 40),
            ForeColor = Color.White
        };

        // 全体設定ノード
        var allSettingsNode = new TreeNode("全体設定") { Tag = "common" };
        treeView.Nodes.Add(allSettingsNode);

        // 詳細パネル
        var detailPanel = new Panel
        {
            Dock = DockStyle.Right,
            Width = 400,
            BackColor = Color.FromArgb(50, 50, 50)
        };

        var selectedLabel = new Label
        {
            Text = "項目を選択してください",
            Dock = DockStyle.Top,
            Padding = new Padding(15),
            Font = new Font("Noto Sans", 10F, FontStyle.Bold),
            ForeColor = Color.White
        };
        detailPanel.Controls.Add(selectedLabel);

        // 画像一覧表示用のパネル
        var imageListPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(15),
            Visible = false
        };
        detailPanel.Controls.Add(imageListPanel);

        // 共通画像の一覧を作成するための情報
        var assets = new[]
        {
            ("danPlatePath", "Plate"),
            ("danPanelSidePath", "PanelSide"),
            ("danTitlePlatePath", "TitlePlate"),
            ("danMiniPlatePath", "MiniPlate")
        };

        // ツリー選択イベント
        treeView.AfterSelect += (s, e) =>
        {
            // パネルをクリア
            imageListPanel.Controls.Clear();
            selectedLabel.Text = "項目を選択してください";
            imageListPanel.Visible = false;

            if (e.Node?.Tag is string tag && tag == "common")
            {
                // 全体設定ノードが選択された場合
                selectedLabel.Text = "全体設定";
                imageListPanel.Visible = true;
                imageListPanel.BringToFront();

                // 画像の一覧を作成
                foreach (var (key, name) in assets)
                {
                    var row = new Panel { Width = 360, Height = 45 };

                    var lbl = new Label
                    {
                        Text = name,
                        Location = new Point(0, 10),
                        Width = 100,
                        Font = new Font("Noto Sans", 9F),
                        ForeColor = Color.White
                    };

                    var txt = new TextBox
                    {
                        Text = _convertAssetAssignments.GetValueOrDefault(key) ?? "",
                        Location = new Point(105, 7),
                        Width = 120,
                        ReadOnly = true,
                        BackColor = Color.FromArgb(60, 60, 60),
                        ForeColor = Color.White
                    };

                    var btnBrowse = new Button
                    {
                        Text = "参照",
                        Location = new Point(230, 4),
                        Width = 55,
                        Height = 25,
                        BackColor = Color.FromArgb(0, 122, 204),
                        ForeColor = Color.White,
                        FlatStyle = FlatStyle.Flat
                    };

                    var btnClear = new Button
                    {
                        Text = "クリア",
                        Location = new Point(290, 4),
                        Width = 55,
                        Height = 25,
                        BackColor = Color.FromArgb(200, 50, 50),
                        ForeColor = Color.White,
                        FlatStyle = FlatStyle.Flat
                    };

                    var keyCapture = key;
                    btnBrowse.Click += (s, e) =>
                    {
                        using var ofd = new OpenFileDialog
                        {
                            Filter = "画像ファイル (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|すべてのファイル (*.*)|*.*",
                            Title = "画像を選択"
                        };
                        if (ofd.ShowDialog(dialog) == DialogResult.OK)
                        {
                            txt.Text = ofd.FileName;
                            _convertAssetAssignments[keyCapture] = ofd.FileName;
                        }
                    };

                    btnClear.Click += (s, e) =>
                    {
                        txt.Text = "";
                        _convertAssetAssignments.Remove(keyCapture);
                    };

                    row.Controls.Add(lbl);
                    row.Controls.Add(txt);
                    row.Controls.Add(btnBrowse);
                    row.Controls.Add(btnClear);
                    imageListPanel.Controls.Add(row);
                }
            }
        };

        // 全ノード展開
        treeView.ExpandAll();

        // レイアウト
        var btnOk = new Button { Text = "保存", Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.OK };
        btnOk.Font = new Font("Noto Sans", 10F, FontStyle.Bold);
        btnOk.BackColor = Color.FromArgb(0, 153, 255);
        btnOk.ForeColor = Color.White;
        btnOk.FlatStyle = FlatStyle.Flat;

        dialog.Controls.Add(treeView);
        dialog.Controls.Add(detailPanel);
        dialog.Controls.Add(btnOk);

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SaveSettings();
        }
    }

    private void InitializeCancelUi()
    {
        _cancelStatusLink = new ToolStripStatusLabel
        {
            IsLink = true,
            Text = LanguageManager.GetString("Interrupt"),
            Enabled = false,
            ForeColor = Color.Red
        };
        _cancelStatusLink.Click += (s, e) =>
        {
            if (_operationCts == null || _operationCts.IsCancellationRequested) return;
            _operationCts.Cancel();
            Log(LanguageManager.GetString("Interrupted"));
        };
        statusStrip.Items.Add(_cancelStatusLink);
    }

    private CancellationToken BeginOperation()
    {
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        if (_cancelStatusLink != null) _cancelStatusLink.Enabled = true;
        SetActionButtonsEnabled(false);
        return _operationCts.Token;
    }

    private void EndOperation()
    {
        _operationCts?.Dispose();
        _operationCts = null;
        if (_cancelStatusLink != null) _cancelStatusLink.Enabled = false;
        SetActionButtonsEnabled(true);
        ResetProgress();
    }

    private void SetIndeterminateProgress(bool visible)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => SetIndeterminateProgress(visible)));
            return;
        }

        progressBar.Style = visible ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
        progressBar.Visible = visible;
    }

    private void SetProgressValue(int current, int total)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => SetProgressValue(current, total)));
            return;
        }

        progressBar.Style = ProgressBarStyle.Blocks;
        progressBar.Visible = total > 0;
        progressBar.Maximum = total <= 0 ? 1 : total;
        progressBar.Value = Math.Max(0, Math.Min(progressBar.Maximum, current));
    }

    private void ResetProgress()
    {
        if (InvokeRequired)
        {
            Invoke(new Action(ResetProgress));
            return;
        }

        progressBar.Style = ProgressBarStyle.Blocks;
        progressBar.Visible = false;
        progressBar.Value = 0;
    }

    private bool ShowCategoryDialog()
    {
        using var dialog = new Form
        {
            Text = LanguageManager.GetString("CategorySelect"),
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(360, 380),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var checkedList = new CheckedListBox
        {
            Dock = DockStyle.Top,
            Height = 290,
            CheckOnClick = true
        };

        var categoryMap = GetCategoryMap();
        foreach (var cat in categoryMap)
        {
            checkedList.Items.Add(cat.DisplayName, _selectedSourceCategories.Contains(cat.InternalName));
        }

        var btnAll = new Button { Text = LanguageManager.GetString("All"), Location = new Point(12, 300), Size = new Size(80, 30) };
        var btnNone = new Button { Text = LanguageManager.GetString("None"), Location = new Point(100, 300), Size = new Size(80, 30) };
        var btnOk = new Button { Text = LanguageManager.GetString("OK"), Location = new Point(190, 340), Size = new Size(75, 30), DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = LanguageManager.GetString("Cancel"), Location = new Point(273, 340), Size = new Size(75, 30), DialogResult = DialogResult.Cancel };

        btnAll.Click += (s, e) =>
        {
            for (var i = 0; i < checkedList.Items.Count; i++) checkedList.SetItemChecked(i, true);
        };
        btnNone.Click += (s, e) =>
        {
            for (var i = 0; i < checkedList.Items.Count; i++) checkedList.SetItemChecked(i, false);
        };

        dialog.Controls.Add(checkedList);
        dialog.Controls.Add(btnAll);
        dialog.Controls.Add(btnNone);
        dialog.Controls.Add(btnOk);
        dialog.Controls.Add(btnCancel);
        dialog.AcceptButton = btnOk;
        dialog.CancelButton = btnCancel;

        if (dialog.ShowDialog(this) != DialogResult.OK) return false;

        _selectedSourceCategories.Clear();
        for (int i = 0; i < checkedList.Items.Count; i++)
        {
            if (checkedList.GetItemChecked(i))
            {
                _selectedSourceCategories.Add(categoryMap[i].InternalName);
            }
        }

        if (_selectedSourceCategories.Count == 0)
        {
            foreach (var category in SongSorterCore.SourceCategories)
                _selectedSourceCategories.Add(category);
        }

        return true;
    }

    private void UpdateCategoryButtonText()
    {
        if (_btnCategorySelect == null) return;
        var total = SongSorterCore.SourceCategories.Length;
        var selected = _selectedSourceCategories.Count == 0 ? total : _selectedSourceCategories.Count;
        _btnCategorySelect.Text = selected >= total ? LanguageManager.GetString("CategoryAll") : string.Format(LanguageManager.GetString("CategoryCount"), selected, total);
    }

    private IReadOnlyCollection<string> GetSelectedSourceCategories()
    {
        if (_selectedSourceCategories.Count == 0)
            return SongSorterCore.SourceCategories;
        return _selectedSourceCategories.ToArray();
    }

    private IReadOnlyCollection<string> GetSelectedFetchFileNames()
    {
        var selected = new HashSet<string>(GetSelectedSourceCategories(), StringComparer.OrdinalIgnoreCase);
        return GetCategoryMap()
            .Where(m => selected.Contains(m.InternalName))
            .Select(m => m.FetchFileName)
            .ToArray();
    }

    private async Task OnFetchListsClick(bool isAutomatic = false)
    {
        var ct = BeginOperation();
        SetStatus(LanguageManager.GetString("FetchingList"), true);
        SetIndeterminateProgress(true);
        Log(isAutomatic ? "起動時の自動譜面リスト更新を開始します。" : LanguageManager.GetString("StartFetchList"));

        try
        {
            var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SongConverter");
            var exportDir = Path.Combine(appDataDir, "Export");
            var categoryMap = GetCategoryMap();
            Directory.CreateDirectory(appDataDir);
            Directory.CreateDirectory(exportDir);

            foreach (var cat in categoryMap)
            {
                if (!_selectedSourceCategories.Contains(cat.InternalName)) continue;

                Log(string.Format(LanguageManager.GetString("Processing"), cat.FetchFileName));
                var songs = await SongListFetcher.FetchSongsAsync(cat.FetchFileName, ct);
                
                var filePath = Path.Combine(exportDir, cat.FetchFileName.Replace(".php", ".txt"));
                var lines = songs.Select(s => $"{s.Title}\t{s.Subtitle}");
                await File.WriteAllLinesAsync(filePath, lines, Utf8NoBom, ct);
            }

            Log(LanguageManager.GetString("FetchDone"));
            if (!isAutomatic)
            {
                MessageBox.Show(LanguageManager.GetString("FetchDone"), LanguageManager.GetString("Done"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (OperationCanceledException)
        {
            Log(LanguageManager.GetString("UserCancelled"));
            if (!isAutomatic)
            {
                MessageBox.Show(LanguageManager.GetString("UserCancelled"), LanguageManager.GetString("Interrupt"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            Log(string.Format(LanguageManager.GetString("Error") + ": {0}", ex.Message));
            MessageBox.Show(ex.Message, LanguageManager.GetString("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            EndOperation();
            SetStatus(LanguageManager.GetString("Wait"));
        }
    }

    private async Task OnOrganizeClick()
    {
        if (string.IsNullOrWhiteSpace(txtTempSongs.Text) || string.IsNullOrWhiteSpace(txtTaikoRoot.Text))
        {
            MessageBox.Show(LanguageManager.GetString("WarnSelectSourceSimu"), LanguageManager.GetString("Warn"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var ct = BeginOperation();
        SetStatus(LanguageManager.GetString("Organizing"), true);
        SetIndeterminateProgress(true);
        Log(LanguageManager.GetString("StartOrganize"));

        try
            {
                var runId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var result = await Task.Run(() => 
                    SongSorterCore.OrganizeSongsDetailed(
                        txtTempSongs.Text, 
                        txtTaikoRoot.Text, 
                        runId, 
                        GetSelectedSourceCategories(), 
                        Log, 
                        ct), ct);

                Log(result.Summary);
            }
        catch (OperationCanceledException)
        {
            Log(LanguageManager.GetString("UserCancelled"));
            MessageBox.Show(LanguageManager.GetString("UserCancelled"), LanguageManager.GetString("Interrupt"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log(string.Format(LanguageManager.GetString("Error") + ": {0}", ex.Message));
            MessageBox.Show(ex.Message, LanguageManager.GetString("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            EndOperation();
            SetStatus(LanguageManager.GetString("Wait"));
        }
    }

    private async Task OnGenerateDanClick()
    {
        if (string.IsNullOrWhiteSpace(txtWikiUrl.Text) || string.IsNullOrWhiteSpace(txtDanSongsPath.Text))
        {
            MessageBox.Show(LanguageManager.GetString("WarnInputWikiSimu"), LanguageManager.GetString("Warn"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(txtDanOutputFolder.Text))
        {
            MessageBox.Show(LanguageManager.GetString("WarnSelectOutput"), LanguageManager.GetString("Warn"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var ct = BeginOperation();
        SetStatus(LanguageManager.GetString("DanGenerating"), true);
        SetIndeterminateProgress(true);
        Log(LanguageManager.GetString("StartDanGenerate"));

        try
        {
            int? danIndex = int.TryParse(txtDanGeneratorIndex.Text, out int idx) ? idx : null;
            await DanGeneratorCore.GenerateAsync(
                        txtWikiUrl.Text,
                        txtDanOutputFolder.Text,
                        txtDanSongsPath.Text,
                        txtWikiFilter.Text,
                        Log,
                        _commonImageSettings,
                        _danImageSettings,
                        danIndex,
                        ct);

            Log(LanguageManager.GetString("DanGenerateDone"));
            Log(string.Format(LanguageManager.GetString("OutputFolder") + " {0}", txtDanOutputFolder.Text));
            MessageBox.Show(LanguageManager.GetString("DanGenerateDone"), LanguageManager.GetString("Done"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            Log(LanguageManager.GetString("UserCancelled"));
            MessageBox.Show(LanguageManager.GetString("UserCancelled"), LanguageManager.GetString("Interrupt"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log(string.Format(LanguageManager.GetString("Error") + ": {0}", ex.Message));
            MessageBox.Show(ex.Message, LanguageManager.GetString("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            EndOperation();
            SetStatus(LanguageManager.GetString("Wait"));
        }
    }

    private async Task OnConvertDanClick()
    {
        if (string.IsNullOrWhiteSpace(txtTjaFile.Text))
        {
            MessageBox.Show(LanguageManager.GetString("WarnSelectTja"), LanguageManager.GetString("Warn"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(txtDanConvertOutputFolder.Text))
        {
            MessageBox.Show(LanguageManager.GetString("WarnSelectOutput"), LanguageManager.GetString("Warn"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var ct = BeginOperation();
        SetStatus(LanguageManager.GetString("ConvertingTja"), true);
        SetIndeterminateProgress(true);
        Log(LanguageManager.GetString("StartTjaConvert"));

        try
        {
            string outputRoot = txtDanConvertOutputFolder.Text.Trim();

            var paths = txtTjaFile.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var tjaFiles = new List<string>();

            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    tjaFiles.AddRange(Directory.GetFiles(path, "*.tja", SearchOption.AllDirectories));
                }
                else if (File.Exists(path))
                {
                    if (path.EndsWith(".tja", StringComparison.OrdinalIgnoreCase))
                        tjaFiles.Add(path);
                }
            }

            if (tjaFiles.Count == 0)
            {
                Log(LanguageManager.GetString("NoValidTja"));
                return;
            }

            Log(string.Format(LanguageManager.GetString("ProcessingCount"), tjaFiles.Count));

            int? danIndex = int.TryParse(txtDanConvertorIndex.Text, out int idx) ? idx : null;
            string? miniPlateText = string.IsNullOrWhiteSpace(txtDanMiniPlateText.Text) ? null : txtDanMiniPlateText.Text.Trim();
            string simuFolder = string.IsNullOrWhiteSpace(txtDanConvertSimu.Text) ? "" : txtDanConvertSimu.Text;
            int current = 0;
            foreach (var tja in tjaFiles)
            {
                ct.ThrowIfCancellationRequested();
                SetProgressValue(++current, tjaFiles.Count);
                Log(string.Format(LanguageManager.GetString("ProcessingFile"), Path.GetFileName(tja)));
                await DanConvertorCore.ConvertAsync(tja, outputRoot, simuFolder, Log, _convertAssetAssignments, danIndex, miniPlateText, ct);
            }

            Log(LanguageManager.GetString("ConvertDone"));
            MessageBox.Show(string.Format(LanguageManager.GetString("ConvertDoneCount"), tjaFiles.Count), LanguageManager.GetString("Done"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            Log(LanguageManager.GetString("Interrupted"));
        }
        catch (Exception ex)
        {
            Log(string.Format(LanguageManager.GetString("Error") + ": {0}", ex.Message));
            MessageBox.Show(ex.Message, LanguageManager.GetString("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetStatus(LanguageManager.GetString("Wait"));
            EndOperation();
        }
    }

    private async Task OnExecuteAddSongsClick()
    {
        if (string.IsNullOrWhiteSpace(txtAddSongsFolder.Text))
        {
            MessageBox.Show(LanguageManager.GetString("WarnSelectWorkFolder"), LanguageManager.GetString("Warn"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var ct = BeginOperation();
        SetStatus(LanguageManager.GetString("AddSongsSyncing"), true);
        SetIndeterminateProgress(true);
        Log(LanguageManager.GetString("StartAddSongsSync"));

        try
        {
            bool gitInstalled = await CheckGitInstalledAsync();
            if (!gitInstalled)
            {
                Log(LanguageManager.GetString("GitNotFound"));
                Log("URL: https://git-scm.com/");
                MessageBox.Show(LanguageManager.GetString("GitNeeded"), LanguageManager.GetString("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var targetDir = txtAddSongsFolder.Text;
            Directory.CreateDirectory(targetDir);

            var songsDir = Path.Combine(targetDir, "Songs");
            var gitDir = Path.Combine(songsDir, ".git");

            if (Directory.Exists(gitDir))
            {
                Log(LanguageManager.GetString("ExistingRepoFound"));
                await RunGitPullAsync(targetDir, ct);
            }
            else if (Directory.Exists(songsDir))
            {
                throw new Exception("Songs folder exists but is not a Git repository.");
            }
            else
            {
                Log(LanguageManager.GetString("CloneRepo"));
                await RunGitCloneAsync(targetDir, ct);
            }

            Log(LanguageManager.GetString("AddSongsSyncDone"));
            MessageBox.Show(LanguageManager.GetString("AddSongsSyncDone"), LanguageManager.GetString("Done"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            Log(LanguageManager.GetString("UserCancelled"));
            MessageBox.Show(LanguageManager.GetString("UserCancelled"), LanguageManager.GetString("Interrupt"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log(string.Format(LanguageManager.GetString("Error") + ": {0}", ex.Message));
            MessageBox.Show(ex.Message, LanguageManager.GetString("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            EndOperation();
            SetStatus(LanguageManager.GetString("Wait"));
        }
    }

    private async Task<bool> CheckGitInstalledAsync()
    {
#if TEST
        return false;
#else
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "git";
            process.StartInfo.Arguments = "--version";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
#endif
    }

    private async Task RunGitCloneAsync(string workingDir, CancellationToken ct)
    {
        // 浅いクローン (depth=1) で帯域を節約し、かつ --no-tags でタグを取得しない
        const int maxRetries = 3;
        int attempt = 0;

        while (true)
        {
            try
            {
                attempt++;
                Log($"クローン試行 {attempt}/{maxRetries}...");
                
                await RunGitCommandAsync(
                    workingDir, 
                    "clone --progress --depth=1 --no-tags https://ese.tjadataba.se/ESE/ESE.git Songs", 
                    ct);
                
                Log("クローン成功！");
                break;
            }
            catch (Exception ex)
            {
                if (attempt >= maxRetries)
                {
                    Log($"最大リトライ回数 {maxRetries} に達しました。");
                    throw;
                }
                
                Log($"クローン失敗: {ex.Message}");
                Log($"5秒後に再試行します...");
                
                // 失敗した場合は不完全なディレクトリを削除
                var songsDir = Path.Combine(workingDir, "Songs");
                if (Directory.Exists(songsDir))
                {
                    try
                    {
                        Directory.Delete(songsDir, true);
                        Log("不完全なディレクトリを削除しました。");
                    }
                    catch (Exception deleteEx)
                    {
                        Log($"ディレクトリ削除失敗: {deleteEx.Message}");
                    }
                }
                
                await Task.Delay(5000, ct);
            }
        }
    }

    private async Task RunGitPullAsync(string workingDir, CancellationToken ct)
    {
        const int maxRetries = 3;
        int attempt = 0;

        while (true)
        {
            try
            {
                attempt++;
                Log($"プル試行 {attempt}/{maxRetries}...");
                
                await RunGitCommandAsync(
                    workingDir, 
                    "-C Songs fetch --depth=1", 
                    ct);
                
                await RunGitCommandAsync(
                    workingDir, 
                    "-C Songs reset --hard origin/HEAD", 
                    ct);
                
                Log("プル成功！");
                break;
            }
            catch (Exception ex)
            {
                if (attempt >= maxRetries)
                {
                    Log($"最大リトライ回数 {maxRetries} に達しました。");
                    throw;
                }
                
                Log($"プル失敗: {ex.Message}");
                Log($"5秒後に再試行します...");
                await Task.Delay(5000, ct);
            }
        }
    }

    private async Task RunGitCommandAsync(string workingDir, string arguments, CancellationToken ct)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo.FileName = "git";
        process.StartInfo.Arguments = arguments;
        process.StartInfo.WorkingDirectory = workingDir;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        
        // パスワード入力を促してハングするのを防ぐ
        process.StartInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
        // ネットワーク安定化のためのタイムアウト設定
        process.StartInfo.EnvironmentVariables["GIT_HTTP_LOW_SPEED_LIMIT"] = "1000";
        process.StartInfo.EnvironmentVariables["GIT_HTTP_LOW_SPEED_TIME"] = "30";
        process.StartInfo.EnvironmentVariables["GIT_SSL_NO_VERIFY"] = "0"; // SSL検証は有効のまま

        process.OutputDataReceived += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) Log(e.Data.Trim());
        };
        process.ErrorDataReceived += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) Log(e.Data.Trim());
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var reg = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited) process.Kill(true);
            }
            catch { }
        });

        await process.WaitForExitAsync();
        ct.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Git コマンドが失敗しました (終了コード: {process.ExitCode}): git {arguments}");
        }
    }

    private void SaveSettings()
    {
        try
        {
            // 共通設定をシリアル化
            var commonJson = JsonSerializer.Serialize(_commonImageSettings);

            // 段位ごとの設定をシリアル化
            var danJson = JsonSerializer.Serialize(_danImageSettings);

            var settings = new AppSettings
            {
                AddSongsFolder = txtAddSongsFolder.Text,
                TempSongs = txtTempSongs.Text,
                TaikoRoot = txtTaikoRoot.Text,
                DanSongsPath = txtDanSongsPath.Text,
                DanConvertSimu = "", // 記憶しない
                WikiUrl = txtWikiUrl.Text,
                DanOutputFolder = txtDanOutputFolder.Text,
                DanConvertOutputFolder = txtDanConvertOutputFolder.Text,
                WikiFilter = "", // 記憶しない
                TjaFile = "", // 記憶しない
                DanGeneratorIndex = "", // 記憶しない
                DanConvertorIndex = txtDanConvertorIndex.Text,
                DanMiniPlateText = txtDanMiniPlateText.Text,
                SelectedCategoriesCsv = string.Join("|", GetSelectedSourceCategories()),
                ConvertAssetsJson = JsonSerializer.Serialize(_convertAssetAssignments),
                // 旧バージョン互換性のため残しておく（空で保存）
                PlateAssignments = new Dictionary<string, string>(),
                OtherImagesJson = "",
                // 新しいデータ構造
                CommonImageSettingsJson = commonJson,
                DanImageSettingsJson = danJson,
                Language = LanguageManager.CurrentLanguage.ToString()
            };

            var json = JsonSerializer.Serialize(settings);
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(SettingsPath, json, Utf8NoBom);
        }
        catch
        {
        }
    }

    private void ChangeLanguage(Language lang)
    {
        LanguageManager.SetLanguage(lang);
        ApplyLocalization();
        UpdateCategoryButtonText();
        SaveSettings();
    }

    private void ApplyLocalization()
    {
        this.Text = "SongConverter";
        menuLanguage.Text = "Language";
        menuJapanese.Text = "日本語 (Japanese)";
        menuEnglish.Text = "English";
        statusLanguageButton.Text = "Language";
        statusJapaneseMenu.Text = "日本語 (Japanese)";
        statusEnglishMenu.Text = "English";
        
        // Tabs
        tabAddSongs.Text = LanguageManager.GetString("TabAddSongs");
        tabSongSorter.Text = LanguageManager.GetString("TabSongSorter");
        tabDanGenerator.Text = LanguageManager.GetString("TabDanGenerator");
        tabDanConvertor.Text = LanguageManager.GetString("TabDanConvertor");

        // Add Songs
        lblAddSongsFolder.Text = LanguageManager.GetString("SelectDownloadFolder");
        btnBrowseAddSongsFolder.Text = LanguageManager.GetString("Browse");
        btnExecuteAddSongs.Text = LanguageManager.GetString("ExecuteAddSongs");

        // Song Sorter
        lblTempSongs.Text = LanguageManager.GetString("SelectSourceSongs");
        btnBrowseTemp.Text = LanguageManager.GetString("Browse");
        lblTaikoRoot.Text = LanguageManager.GetString("SelectDestSongs");
        btnBrowseRoot.Text = LanguageManager.GetString("Browse");
        btnOrganize.Text = LanguageManager.GetString("StartSort");
        if (_btnCategorySelect != null) UpdateCategoryButtonText();

        // Dan Generator
        lblWikiUrl.Text = LanguageManager.GetString("WikiUrl");
        lblWikiFilter.Text = LanguageManager.GetString("FilterDan");
        lblDanGeneratorIndex.Text = LanguageManager.GetString("DanIndex");
        lblDanOutputFolder.Text = LanguageManager.GetString("OutputFolder");
        btnBrowseDanOutputFolder.Text = LanguageManager.GetString("Browse");
        lblDanSongsPath.Text = LanguageManager.GetString("SelectSongsFolder");
        btnBrowseDanSongs.Text = LanguageManager.GetString("Browse");
        btnGenerateDan.Text = LanguageManager.GetString("GenerateDan");
        if (_btnImageSelect != null) _btnImageSelect.Text = "画像選択";

        // Dan Convertor
        lblTjaFile.Text = LanguageManager.GetString("TjaFile");
        btnBrowseTjaFile.Text = LanguageManager.GetString("Browse");
        lblDanConvertOutputFolder.Text = LanguageManager.GetString("DanConvertOutputFolder");
        btnBrowseDanConvertOutputFolder.Text = LanguageManager.GetString("Browse");
        lblDanConvertorIndex.Text = LanguageManager.GetString("DanIndex");
        lblDanMiniPlateText.Text = LanguageManager.GetString("MiniPlateText");
        lblDanConvertSimu.Text = LanguageManager.GetString("DanConvertSimu");
        btnBrowseDanConvertSimu.Text = LanguageManager.GetString("Browse");
        btnConvertDan.Text = LanguageManager.GetString("ExecuteConvert");
        if (_btnConvertAssetSelect != null) _btnConvertAssetSelect.Text = LanguageManager.GetString("SelectImage");

        // Status
        var currentStatus = statusLabel.Text;
        if (string.IsNullOrEmpty(currentStatus) || 
            currentStatus == "準備完了" || currentStatus == "準備完了。" || 
            currentStatus == "Ready" || currentStatus == "Ready." || 
            currentStatus == "待機中" || currentStatus == "Idle")
        {
            statusLabel.Text = LanguageManager.GetString("Ready");
        }
    }

    private void LoadSettings()
    {
        if (!File.Exists(SettingsPath))
        {
            return;
        }

        try
        {
            var json = ReadTextWithFallback(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            if (settings == null)
            {
                return;
            }

            txtAddSongsFolder.Text = settings.AddSongsFolder ?? "";
            txtTempSongs.Text = settings.TempSongs ?? "";
            txtTaikoRoot.Text = settings.TaikoRoot ?? "";
            txtDanSongsPath.Text = settings.DanSongsPath ?? "";
            txtDanConvertSimu.Text = "";
            txtTjaFile.Text = "";
            
            txtDanOutputFolder.Text = settings.DanOutputFolder ?? "";
            txtDanConvertOutputFolder.Text = settings.DanConvertOutputFolder ?? "";

            txtWikiUrl.Text = settings.WikiUrl ?? "";
            txtWikiFilter.Text = "";
            txtDanGeneratorIndex.Text = "";
            txtDanConvertorIndex.Text = settings.DanConvertorIndex ?? "";
            txtDanMiniPlateText.Text = settings.DanMiniPlateText ?? "";

            if (!string.IsNullOrEmpty(settings.Language) && Enum.TryParse<Language>(settings.Language, out var lang))
            {
                LanguageManager.SetLanguage(lang);
            }

            _selectedSourceCategories.Clear();
            var raw = settings.SelectedCategoriesCsv ?? string.Empty;
            var parts = raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (SongSorterCore.SourceCategories.Contains(part, StringComparer.OrdinalIgnoreCase))
                    _selectedSourceCategories.Add(part);
            }

            if (_selectedSourceCategories.Count == 0)
            {
                foreach (var category in SongSorterCore.SourceCategories)
                    _selectedSourceCategories.Add(category);
            }

            if (!string.IsNullOrEmpty(settings.ConvertAssetsJson))
            {
                var assets = JsonSerializer.Deserialize<Dictionary<string, string>>(settings.ConvertAssetsJson);
                if (assets != null)
                {
                    _convertAssetAssignments.Clear();
                    foreach (var kvp in assets) _convertAssetAssignments[kvp.Key] = kvp.Value;
                }
            }

            // 新しいデータ構造で読み込み
            bool loadedNew = false;
            if (!string.IsNullOrEmpty(settings.CommonImageSettingsJson))
            {
                try
                {
                    var common = JsonSerializer.Deserialize<Core.DanGeneratorCore.DanImageSettings>(settings.CommonImageSettingsJson);
                    if (common != null)
                    {
                        _commonImageSettings.PlatePath = common.PlatePath;
                        _commonImageSettings.PanelSidePath = common.PanelSidePath;
                        _commonImageSettings.TitlePlatePath = common.TitlePlatePath;
                        _commonImageSettings.MiniPlatePath = common.MiniPlatePath;
                    }
                }
                catch
                {
                }
            }

            if (!string.IsNullOrEmpty(settings.DanImageSettingsJson))
            {
                try
                {
                    var danSettings = JsonSerializer.Deserialize<Dictionary<string, Core.DanGeneratorCore.DanImageSettings>>(settings.DanImageSettingsJson);
                    if (danSettings != null)
                    {
                        _danImageSettings.Clear();
                        foreach (var kvp in danSettings)
                        {
                            _danImageSettings[kvp.Key] = kvp.Value;
                        }
                        loadedNew = true;
                    }
                }
                catch
                {
                }
            }

            // 旧バージョンの設定から移行（新しい設定が読み込めなかった場合）
            if (!loadedNew)
            {
                // 旧 PlateAssignments から移行
                if (settings.PlateAssignments != null && settings.PlateAssignments.Count > 0)
                {
                    foreach (var kvp in settings.PlateAssignments)
                    {
                        if (kvp.Key == "*")
                        {
                            _commonImageSettings.PlatePath = kvp.Value;
                        }
                        else
                        {
                            if (!_danImageSettings.TryGetValue(kvp.Key, out var danSettings))
                            {
                                danSettings = new Core.DanGeneratorCore.DanImageSettings();
                                _danImageSettings[kvp.Key] = danSettings;
                            }
                            danSettings.PlatePath = kvp.Value;
                        }
                    }
                }

                // 旧 OtherImagesJson から移行（共通設定として）
                if (!string.IsNullOrEmpty(settings.OtherImagesJson))
                {
                    try
                    {
                        var otherImages = JsonSerializer.Deserialize<Dictionary<string, string>>(settings.OtherImagesJson);
                        if (otherImages != null && otherImages.Count > 0)
                        {
                            if (otherImages.TryGetValue("danPlatePath", out var platePath))
                                _commonImageSettings.PlatePath = platePath;
                            if (otherImages.TryGetValue("danPanelSidePath", out var panelSidePath))
                                _commonImageSettings.PanelSidePath = panelSidePath;
                            if (otherImages.TryGetValue("danTitlePlatePath", out var titlePlatePath))
                                _commonImageSettings.TitlePlatePath = titlePlatePath;
                            if (otherImages.TryGetValue("danMiniPlatePath", out var miniPlatePath))
                                _commonImageSettings.MiniPlatePath = miniPlatePath;
                        }
                    }
                    catch
                    {
                    }
                }

                // ConvertAssetsJson からも共通設定に移行（OtherImagesJson が空の場合）
                if (string.IsNullOrEmpty(_commonImageSettings.PlatePath) && !string.IsNullOrEmpty(settings.ConvertAssetsJson))
                {
                    try
                    {
                        var convertAssets = JsonSerializer.Deserialize<Dictionary<string, string>>(settings.ConvertAssetsJson);
                        if (convertAssets != null)
                        {
                            if (convertAssets.TryGetValue("danPlatePath", out var platePath))
                                _commonImageSettings.PlatePath = platePath;
                            if (convertAssets.TryGetValue("danPanelSidePath", out var panelSidePath))
                                _commonImageSettings.PanelSidePath = panelSidePath;
                            if (convertAssets.TryGetValue("danTitlePlatePath", out var titlePlatePath))
                                _commonImageSettings.TitlePlatePath = titlePlatePath;
                            if (convertAssets.TryGetValue("danMiniPlatePath", out var miniPlatePath))
                                _commonImageSettings.MiniPlatePath = miniPlatePath;
                        }
                    }
                    catch
                    {
                    }
                }

                // 移行が完了したら新しい形式で保存
                SaveSettings();
            }
        }
        catch
        {
        }
    }

    private static string ReadTextWithFallback(string path)
    {
        try
        {
            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return File.ReadAllText(path, Encoding.GetEncoding(932));
        }
    }

    class AppSettings
    {
        public string AddSongsFolder { get; set; } = "";
        public string TempSongs { get; set; } = "";
        public string TaikoRoot { get; set; } = "";
        public string DanSongsPath { get; set; } = "";
        public string DanConvertSimu { get; set; } = "";
        public string WikiUrl { get; set; } = "";
        public string DanOutputFolder { get; set; } = "";
        public string DanConvertOutputFolder { get; set; } = "";
        public string WikiFilter { get; set; } = "";
        public string TjaFile { get; set; } = "";
        public string DanGeneratorIndex { get; set; } = "";
        public string DanConvertorIndex { get; set; } = "";
        public string DanMiniPlateText { get; set; } = "";
        public string SelectedCategoriesCsv { get; set; } = "";
        public string ConvertAssetsJson { get; set; } = "";
        public string Language { get; set; } = "";
        // 旧バージョン互換性のため残しておく
        public Dictionary<string, string> PlateAssignments { get; set; } = new Dictionary<string, string>();
        public string OtherImagesJson { get; set; } = "";
        // 新しいデータ構造
        public string CommonImageSettingsJson { get; set; } = "";
        public string DanImageSettingsJson { get; set; } = "";
    }



    private async Task CheckForUpdateOnStartupAsync()
    {
        try
        {
            Log("アップデートチェックを開始します...");
            var (hasUpdate, latestVersion, downloadUrl, currentVersion, debugInfo) = await Updater.CheckForUpdateAsync();
            
            Log($"デバッグ情報: {debugInfo}");

            if (hasUpdate && downloadUrl != null)
            {
                Log($"新しいバージョン {latestVersion} が見つかりました。");
                
                var result = MessageBox.Show(
                    $"新しいバージョン {latestVersion} が見つかりました。\nアップデートしますか？",
                    "アップデートの確認",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    SetStatus("アップデートをダウンロード中...", true);
                    var progress = new Progress<int>(p =>
                    {
                        progressBar.Value = p;
                    });

                    var success = await Updater.DownloadAndUpdateAsync(downloadUrl, progress, CancellationToken.None);

                    if (success)
                    {
                        Log("アップデートを開始します。アプリを終了します...");
                        MessageBox.Show("アップデートを開始します。アプリを終了します。", "アップデート", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Application.Exit();
                    }
                    else
                    {
                        Log("アップデートに失敗しました。");
                        MessageBox.Show("アップデートに失敗しました。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"アップデートチェック中にエラーが発生しました: {ex.Message}");
        }
        finally
        {
            SetStatus(LanguageManager.GetString("Ready"), false);
            SetIndeterminateProgress(false);
            ResetProgress();
        }
    }

    private async void MenuCheckUpdate_Click(object? sender, EventArgs e)
    {
        try
        {
            SetStatus("アップデートを確認中...", true);
            SetIndeterminateProgress(true);
            Log("アップデートチェックを開始します...");

            var (hasUpdate, latestVersion, downloadUrl, currentVersion, debugInfo) = await Updater.CheckForUpdateAsync();
            
            Log($"デバッグ情報: {debugInfo}");

            if (hasUpdate && downloadUrl != null)
            {
                Log($"新しいバージョン {latestVersion} が見つかりました。");
                
                var result = MessageBox.Show(
                    $"新しいバージョン {latestVersion} が見つかりました。\nアップデートしますか？",
                    "アップデートの確認",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    SetStatus("アップデートをダウンロード中...", true);
                    var progress = new Progress<int>(p =>
                    {
                        progressBar.Value = p;
                    });

                    var success = await Updater.DownloadAndUpdateAsync(downloadUrl, progress, CancellationToken.None);

                    if (success)
                    {
                        Log("アップデートを開始します。アプリを終了します...");
                        MessageBox.Show("アップデートを開始します。アプリを終了します。", "アップデート", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Application.Exit();
                    }
                    else
                    {
                        Log("アップデートに失敗しました。");
                        MessageBox.Show("アップデートに失敗しました。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            else
            {
                Log("最新バージョンです。");
                MessageBox.Show("最新バージョンです。", "アップデートの確認", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            Log($"アップデートチェック中にエラーが発生しました: {ex.Message}");
            MessageBox.Show("アップデートチェック中にエラーが発生しました。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetStatus(LanguageManager.GetString("Ready"), false);
            SetIndeterminateProgress(false);
            ResetProgress();
        }
    }
}

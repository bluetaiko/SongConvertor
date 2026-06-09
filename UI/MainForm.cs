using System.Text;
using System.Text.Json;
using SongConverter.Core;

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
    private Button? _btnPlateSelect;
    private Button? _btnConvertAssetSelect;
    private readonly Dictionary<string, string> _plateAssignments = new(StringComparer.OrdinalIgnoreCase);
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
            _plateAssignments.Clear();
            SaveSettings();
        };
        
        // Operations
        btnFetchLists.Click += async (s, e) => await OnFetchListsClick();
        btnOrganize.Click += async (s, e) => await OnOrganizeClick();
        btnGenerateDan.Click += async (s, e) => await OnGenerateDanClick();
        btnExecuteAddSongs.Click += async (s, e) => await OnExecuteAddSongsClick();
        btnConvertDan.Click += async (s, e) => await OnConvertDanClick();

        menuJapanese.Click += (s, e) => ChangeLanguage(Language.Japanese);
        menuEnglish.Click += (s, e) => ChangeLanguage(Language.English);

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

    private void Log(string msg)
    {
        if (this.InvokeRequired)
        {
            this.Invoke(new Action(() => Log(msg)));
            return;
        }
        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
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
        btnFetchLists.Enabled = enabled;
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
            Location = new Point(370, 170),
            Size = new Size(280, 45),
            BackColor = Color.FromArgb(80, 80, 80),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
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
        _btnPlateSelect = new Button
        {
            Name = "btnPlateSelect",
            Text = LanguageManager.GetString("PlateSettings"),
            Size = btnGenerateDan.Size,
            Location = new Point(btnGenerateDan.Right + 10, btnGenerateDan.Top),
            BackColor = Color.FromArgb(80, 80, 80),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = btnGenerateDan.Font
        };
        _btnPlateSelect.Click += async (s, e) => await ShowPlateSelectionDialog();
        tabDanGenerator.Controls.Add(_btnPlateSelect);
    }

    private async Task ShowPlateSelectionDialog()
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
            var ranks = await DanGeneratorCore.FetchRankNamesAsync(txtWikiUrl.Text);
            if (ranks.Count == 0)
            {
                MessageBox.Show(LanguageManager.GetString("DanNameNotFound"), LanguageManager.GetString("Warn"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var dialog = new Form
            {
                Text = LanguageManager.GetString("PlateIndividualSettings"),
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(600, 500),
                FormBorderStyle = FormBorderStyle.Sizable,
                MinimizeBox = false
            };

            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(10)
            };

            // 全体設定用の行
            panel.Controls.Add(CreatePlateRow("*", LanguageManager.GetString("ApplyToAllDan"), _plateAssignments.GetValueOrDefault("*")));
            foreach (var rank in ranks)
            {
                panel.Controls.Add(CreatePlateRow(rank, rank, _plateAssignments.GetValueOrDefault(rank)));
            }

            var btnOk = new Button { Text = LanguageManager.GetString("Save"), Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.OK };
            dialog.Controls.Add(panel);
            dialog.Controls.Add(btnOk);

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                foreach (Control control in panel.Controls)
                {
                    if (control is Panel row && row.Tag is string rName)
                    {
                        var txt = row.Controls.OfType<TextBox>().FirstOrDefault();
                        if (txt != null && !string.IsNullOrWhiteSpace(txt.Text))
                        {
                            _plateAssignments[rName] = txt.Text;
                        }
                        else
                        {
                            _plateAssignments.Remove(rName);
                        }
                    }
                }
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

    private Panel CreatePlateRow(string rankKey, string displayName, string? currentPath)
    {
        var row = new Panel { Width = 550, Height = 35, Tag = rankKey };
        var lbl = new Label { Text = displayName, Location = new Point(0, 5), Width = 180 };
        var txt = new TextBox { Text = currentPath ?? "", Location = new Point(185, 2), Width = 280 };
        var btn = new Button { Text = LanguageManager.GetString("Browse"), Location = new Point(470, 0), Width = 70 };

        btn.Click += (s, e) =>
        {
            using var ofd = new OpenFileDialog { Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All files (*.*)|*.*" };
            if (ofd.ShowDialog() == DialogResult.OK) txt.Text = ofd.FileName;
        };

        row.Controls.Add(lbl);
        row.Controls.Add(txt);
        row.Controls.Add(btn);
        return row;
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
            ClientSize = new Size(580, 240),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        var assets = new[] 
        { 
            ("danPlatePath", "Plate"),
            ("danPanelSidePath", "PanelSide"),
            ("danTitlePlatePath", "TitlePlate"),
            ("danMiniPlatePath", "MiniPlate")
        };

        var textboxes = new Dictionary<string, TextBox>();

        foreach (var (key, label) in assets)
        {
            var row = new Panel { Width = 550, Height = 35 };
            var lbl = new Label { Text = label, Location = new Point(0, 5), Width = 180 };
            var txt = new TextBox { Text = _convertAssetAssignments.GetValueOrDefault(key) ?? "", Location = new Point(185, 2), Width = 280 };
            var btn = new Button { Text = LanguageManager.GetString("Browse"), Location = new Point(470, 0), Width = 70 };

            btn.Click += (s, e) =>
            {
                using var ofd = new OpenFileDialog { Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All files (*.*)|*.*" };
                if (ofd.ShowDialog() == DialogResult.OK) txt.Text = ofd.FileName;
            };

            row.Controls.Add(lbl);
            row.Controls.Add(txt);
            row.Controls.Add(btn);
            panel.Controls.Add(row);
            textboxes[key] = txt;
        }

        var btnOk = new Button { Text = LanguageManager.GetString("Save"), Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.OK };
        dialog.Controls.Add(panel);
        dialog.Controls.Add(btnOk);

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            foreach (var kvp in textboxes)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Value.Text)) _convertAssetAssignments[kvp.Key] = kvp.Value.Text;
                else _convertAssetAssignments.Remove(kvp.Key);
            }
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
        return CategoryMap
            .Where(m => selected.Contains(m.SourceCategory))
            .Select(m => m.FetchFileName)
            .ToArray();
    }

    private async Task OnFetchListsClick()
    {
        var ct = BeginOperation();
        SetStatus(LanguageManager.GetString("FetchingList"), true);
        SetIndeterminateProgress(true);
        Log(LanguageManager.GetString("StartFetchList"));

        try
        {
            var exportDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Export");
            var categoryMap = GetCategoryMap();
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
            MessageBox.Show(LanguageManager.GetString("FetchDone"), LanguageManager.GetString("Done"), MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            Log(string.Format(LanguageManager.GetString("Done") + ": {0}", result.ReportPath));
            MessageBox.Show($"{result.Summary}\n\n{result.ReportPath}", LanguageManager.GetString("Done"), MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            var outputDir = await DanGeneratorCore.GenerateAsync(
                txtWikiUrl.Text,
                txtDanSongsPath.Text,
                txtDanOutputFolder.Text,
                txtWikiFilter.Text,
                _plateAssignments,
                ct);

            Log(LanguageManager.GetString("DanGenerateDone"));
            Log(string.Format(LanguageManager.GetString("OutputFolder") + " {0}", outputDir));
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
            foreach (var tja in tjaFiles)
            {
                ct.ThrowIfCancellationRequested();
                Log(string.Format(LanguageManager.GetString("ProcessingFile"), Path.GetFileName(tja)));
                string simuFolder = string.IsNullOrWhiteSpace(txtDanConvertSimu.Text) ? "" : txtDanConvertSimu.Text;
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

    private Task RunGitCloneAsync(string workingDir, CancellationToken ct)
    {
        return RunGitCommandAsync(workingDir, "clone --progress https://ese.tjadataba.se/ESE/ESE.git Songs", ct);
    }

    private Task RunGitPullAsync(string workingDir, CancellationToken ct)
    {
        return RunGitCommandAsync(workingDir, "-C Songs pull --progress --ff-only", ct);
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
            Language = LanguageManager.CurrentLanguage.ToString()
        };

        var json = JsonSerializer.Serialize(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, json, Utf8NoBom);
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
        menuLanguage.Text = LanguageManager.GetString("Language");
        menuJapanese.Text = "日本語 (Japanese)";
        menuEnglish.Text = "English";
        
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
        btnFetchLists.Text = LanguageManager.GetString("UpdateSongList");
        btnOrganize.Text = LanguageManager.GetString("StartSort");

        // Dan Generator
        lblWikiUrl.Text = LanguageManager.GetString("WikiUrl");
        lblWikiFilter.Text = LanguageManager.GetString("FilterDan");
        lblDanGeneratorIndex.Text = LanguageManager.GetString("DanIndex");
        lblDanOutputFolder.Text = LanguageManager.GetString("OutputFolder");
        btnBrowseDanOutputFolder.Text = LanguageManager.GetString("Browse");
        lblDanSongsPath.Text = LanguageManager.GetString("SelectSongsFolder");
        btnBrowseDanSongs.Text = LanguageManager.GetString("Browse");
        btnGenerateDan.Text = LanguageManager.GetString("GenerateDan");

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

        // Status
        if (statusLabel.Text == "準備完了" || statusLabel.Text == "Ready." || statusLabel.Text == "Idle")
        {
            statusLabel.Text = LanguageManager.GetString("Ready");
        }
    }

    private void LoadSettings()
    {
        if (!File.Exists(SettingsPath)) return;

        try
        {
            var json = ReadTextWithFallback(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            if (settings == null) return;

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
        }
        catch { }
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
    }
}

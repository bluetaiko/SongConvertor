using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using HtmlDoc = HtmlAgilityPack.HtmlDocument;
using SongConverter.Models;
using SongConverter.Utils;

namespace SongConverter.Core;

public class DanGeneratorCore
{
    private static readonly HttpClient httpClient = new HttpClient();

    public static async Task<List<string>> FetchRankNamesAsync(string inputSource, CancellationToken ct = default)
    {
        string html;
        if (inputSource.StartsWith("http"))
        {
            var response = await httpClient.GetAsync(inputSource, ct);
            response.EnsureSuccessStatusCode();
            html = await response.Content.ReadAsStringAsync(ct);
        }
        else
        {
            if (!File.Exists(inputSource)) return new List<string>();
            html = await File.ReadAllTextAsync(inputSource, ct);
        }

        var doc = new HtmlDoc();
        doc.LoadHtml(html);
        var nodes = doc.DocumentNode.SelectNodes("//h3 | //h4 | //table");
        if (nodes == null) return new List<string>();

        var rankNames = new[] { "五級", "四級", "三級", "二級", "一級", "初段", "二段", "三段", "四段", "五段", "六段", "七段", "八段", "九段", "十段", "玄人", "名人", "超人", "達人" };
        var excludeKeywords = new[] { "合格条件", "お題", "お品書き", "魂ゲージ", "たたけた数", "叩けた数", "総音符数", "ノーツ数", "不可", "連打数", "良", "可", "コンボ", "最大コンボ数", "スコア", "動画", "計", "楽曲名", "課題曲", "難易度", "難しさ", "むずかしさ", "強さ", "★", "レベル", "概要", "詳細", "備考", "リンク", "プレイ動画", "参照", "初出", "回数", "解放期間", "解放条件", "QRコード", "QR", "公式サイト", "コメント", "アンケート", "疑問", "解決所", "募集", "募集中", "？", "質問" };

        var detectedRanks = new List<string>();
        string currentVersion = "";
        string currentSection = "";
        bool isGaiden = inputSource.Contains("外伝") || inputSource.Contains("%E5%A4%96%E4%BC%9D");

        foreach (var node in nodes)
        {
            if (node.Name == "h3")
            {
                currentVersion = HtmlEntity.DeEntitize(node.InnerText.Trim());
                continue;
            }
            if (node.Name == "h4")
            {
                currentSection = HtmlEntity.DeEntitize(node.InnerText.Trim());
                continue;
            }
            if (node.Name != "table") continue;

            // GenerateAsyncと同じ除外ロジックを適用
            bool is2020 = currentVersion.Contains("2020");
            bool isCandidate = node.InnerText.Contains("課題候補曲リスト") || currentVersion.Contains("候補") || currentSection.Contains("候補");
            bool isExtraRegion = currentSection.Contains("CHINA") || currentSection.Contains("中国") || currentSection.Contains("アジア") || currentSection.Contains("Asia") || currentSection.Contains("海外") || currentSection.Contains("台湾") || currentSection.Contains("韓国") || currentSection.Contains("版の課題曲") ||
                                 node.InnerText.Contains("中国版") || node.InnerText.Contains("アジア版") || node.InnerText.Contains("版の課題曲");
            if (is2020) isExtraRegion = false;
            bool isChangeLog = currentSection.Contains("変更点") || currentSection.Contains("違い") || currentSection.Contains("差分") || node.InnerText.Contains("変更点");

            if (isCandidate || isExtraRegion || isChangeLog) continue;
            if (!node.InnerText.Contains("1st") && !node.InnerText.Contains("魂ゲージ") && !node.InnerText.Contains("合格条件")) continue;

            var rows = node.SelectNodes(".//tr");
            if (rows == null) continue;

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var cellNodes = row.SelectNodes(".//td");
                if (cellNodes == null) continue;
                var cellTexts = cellNodes.Select(c => HtmlEntity.DeEntitize(c.InnerText.Trim())).ToList();
                if (!cellTexts.Any(t => t.Contains("魂ゲージ") || t.Contains("合格条件") || t.Contains("可") || t.Contains("不可") || t.Contains("叩けた数"))) continue;

                string rank = FindRankNameFromRow(row, rankNames, excludeKeywords, isGaiden);
                if (string.IsNullOrEmpty(rank) && i > 0) rank = FindRankNameFromRow(rows[i - 1], rankNames, excludeKeywords, isGaiden);
                
                if (string.IsNullOrEmpty(rank) && cellTexts.Count > 0)
                {
                    foreach (var cellText in cellTexts) {
                        if (string.IsNullOrEmpty(cellText) || cellText.Length < 2) continue;
                        bool isRankLike = isGaiden ||
                                          rankNames.Any(rn => cellText.Contains(rn)) || 
                                          cellText.EndsWith("級") || cellText.EndsWith("段") || 
                                          cellText == "玄人" || cellText == "名人" || cellText == "超人" || cellText == "達人";
                        if (!isRankLike) continue;
                        if (excludeKeywords.Any(k => cellText.Contains(k))) continue;
                        if (IsInvalidRankName(cellText, isGaiden)) continue;
                        rank = cellText;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(rank))
                {
                    rank = rank.Replace("(裏)", "").Replace("(おに)", "").Replace("(おに裏)", "").Trim();
                    rank = Regex.Replace(rank, @"^[（(]裏[）)]$","").Trim();
                    if (!detectedRanks.Contains(rank)) detectedRanks.Add(rank);
                }
            }
        }
        return detectedRanks;
    }

    public static async Task GenerateAsync(string inputSource, string outputDir, string songsFolder = "", string filter = "", Action<string>? logAction = null, Dictionary<string, string>? plateMap = null, int? danIndexOverride = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

        string html;
        try 
        {
            if (inputSource.StartsWith("http"))
            {
                logAction?.Invoke($"URLからデータを取得中: {inputSource}");
                var response = await httpClient.GetAsync(inputSource, ct);
                response.EnsureSuccessStatusCode();
                html = await response.Content.ReadAsStringAsync(ct);
            }
            else
            {
                if (!File.Exists(inputSource)) {
                    logAction?.Invoke($"エラー: ファイルが見つかりません ({inputSource})");
                    return;
                }
                logAction?.Invoke($"{inputSource} を読み込んでいます...");
                html = await File.ReadAllTextAsync(inputSource, ct);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logAction?.Invoke($"データ取得エラー: {ex.Message}");
            return;
        }

        var doc = new HtmlDoc();
        doc.LoadHtml(html);

        var nodes = doc.DocumentNode.SelectNodes("//h3 | //h4 | //table");
        if (nodes == null)
        {
            logAction?.Invoke("エラー: コンテンツが見つかりません。");
            return;
        }

        var rankNames = new[] { "五級", "四級", "三級", "二級", "一級", "初段", "二段", "三段", "四段", "五段", "六段", "七段", "八段", "九段", "十段", "玄人", "名人", "超人", "達人" };
        
        var excludeKeywords = new[] { "合格条件", "お題", "お品書き", "魂ゲージ", "たたけた数", "叩けた数", "総音符数", "ノーツ数", "不可", "連打数", "良", "可", "コンボ", "最大コンボ数", "スコア", "動画", "計", "楽曲名", "課題曲", "難易度", "難しさ", "むずかしさ", "強さ", "★", "レベル", "概要", "詳細", "備考", "リンク", "プレイ動画", "参照", "初出", "回数", "解放期間", "解放条件", "QRコード", "QR", "公式サイト", "コメント", "アンケート", "疑問", "解決所", "募集", "募集中", "？", "質問" };

        int totalProcessed = 0;
        var missingSongs = new List<string>();
        var allSets = new List<(string versionName, List<(DanCourse course, string detectedRank, int rankIdx)> courses)>();
        var currentSet = new List<(DanCourse course, string detectedRank, int rankIdx)>();
        var seenIndicesInCurrentSet = new HashSet<int>();

        string currentVersion = "";
        string currentSection = "";
        bool isGaiden = inputSource.Contains("外伝") || inputSource.Contains("%E5%A4%96%E4%BC%9D");

        foreach (var node in nodes)
        {
            ct.ThrowIfCancellationRequested();

            if (node.Name == "h3")
            {
                string newVersion = HtmlEntity.DeEntitize(node.InnerText.Trim());
                // バージョンが変わったら現在のセットを保存して新しく始める
                // 外伝の場合は一つの連番にするため、セットを分けない
                if (!isGaiden && !string.IsNullOrEmpty(currentVersion) && newVersion != currentVersion && currentSet.Count > 0)
                {
                    allSets.Add((currentVersion, currentSet));
                    currentSet = new List<(DanCourse course, string detectedRank, int rankIdx)>();
                    seenIndicesInCurrentSet.Clear();
                }
                currentVersion = newVersion;
                continue;
            }
            if (node.Name == "h4")
            {
                currentSection = HtmlEntity.DeEntitize(node.InnerText.Trim());
                continue;
            }
            if (node.Name != "table") continue;

            var table = node;
            // 候補曲リスト、中国版、アジア版、変更点などはスキップ
            // 2020はタイトルに「ASIA」を含むため、除外対象から外すように特別対応
            bool is2020 = currentVersion.Contains("2020");
            
            bool isCandidate = table.InnerText.Contains("課題候補曲リスト") || currentVersion.Contains("候補") || currentSection.Contains("候補");
            
            bool isExtraRegion = currentSection.Contains("CHINA") || currentSection.Contains("中国") || currentSection.Contains("アジア") || currentSection.Contains("Asia") || currentSection.Contains("海外") || currentSection.Contains("台湾") || currentSection.Contains("韓国") || currentSection.Contains("版の課題曲") ||
                                 table.InnerText.Contains("中国版") || table.InnerText.Contains("アジア版") || table.InnerText.Contains("版の課題曲");
            
            // 2020セクションの場合は、海外版キーワードによる除外を無効化する（ユーザー要望）
            if (is2020) isExtraRegion = false;

            bool isChangeLog = currentSection.Contains("変更点") || currentSection.Contains("違い") || currentSection.Contains("差分") || table.InnerText.Contains("変更点");

            if (isCandidate || isExtraRegion || isChangeLog) continue;

            // 段位道場の表であることを確認
            if (!table.InnerText.Contains("1st") && !table.InnerText.Contains("魂ゲージ") && !table.InnerText.Contains("合格条件")) continue;

            var rows = table.SelectNodes(".//tr");
            if (rows == null) continue;

            string lastParsedRankName = "";
            int[] tableActiveRowSpans = new int[30];

            for (int i = 0; i < rows.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var row = rows[i];
                var absoluteCells = GetAbsoluteCells(row, tableActiveRowSpans);
                if (absoluteCells.Count == 0) continue;

                var cellTexts = absoluteCells.Values.Select(c => HtmlEntity.DeEntitize(c.InnerText.Trim())).ToList();

                if (!cellTexts.Any(t => t.Contains("魂ゲージ") || t.Contains("合格条件") || t.Contains("可") || t.Contains("不可") || t.Contains("叩けた数"))) continue;

                string detectedRank = FindRankNameFromRow(row, rankNames, excludeKeywords, isGaiden);

                if (string.IsNullOrEmpty(detectedRank) && i > 0)
                {
                    var aboveRow = rows[i - 1];
                    detectedRank = FindRankNameFromRow(aboveRow, rankNames, excludeKeywords, isGaiden);
                }

                if (string.IsNullOrEmpty(detectedRank) && absoluteCells.Count > 0)
                {
                    foreach (var cell in absoluteCells.Values) {
                        string cellText = HtmlEntity.DeEntitize(cell.InnerText.Trim());
                        if (string.IsNullOrEmpty(cellText) || cellText.Length < 2) continue;
                        // 段位名らしいキーワードを含んでいるかチェック
                        bool isRankLike = isGaiden ||
                                          rankNames.Any(rn => cellText.Contains(rn)) || 
                                          cellText.EndsWith("級") || cellText.EndsWith("段") || 
                                          cellText == "玄人" || cellText == "名人" || cellText == "超人" || cellText == "達人";
                        
                        if (!isRankLike) continue;
                        if (excludeKeywords.Any(k => cellText.Contains(k))) continue;
                        if (IsInvalidRankName(cellText, isGaiden)) continue;
                        detectedRank = cellText;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(detectedRank))
                {
                    detectedRank = detectedRank.Replace("(裏)", "").Replace("(おに)", "").Replace("(おに裏)", "").Trim();
                    detectedRank = Regex.Replace(detectedRank, @"^[（(]裏[）)]$","").Trim();
                }

                if (string.IsNullOrEmpty(detectedRank) || detectedRank == lastParsedRankName) continue;
                if (!string.IsNullOrEmpty(filter) && !detectedRank.Contains(filter)) continue;

                try
                {
                    int rankIdx = danIndexOverride ?? (isGaiden ? 19 : rankNames.ToList().FindIndex(r => detectedRank.Contains(r)));
                    var dan = new DanCourse { title = detectedRank, danIndex = rankIdx >= 0 ? rankIdx : 0 };
                    
                    // セットの区切り判定
                    int lastRankIdx = currentSet.Count > 0 ? currentSet.Last().rankIdx : -1;
                    bool isNewSet = false;

                    if (!isGaiden)
                    {
                        if (rankIdx >= 0)
                        {
                            // すでに見たランク、または現在より低いランクが現れたら新セットとみなす
                            if (seenIndicesInCurrentSet.Contains(rankIdx) || (lastRankIdx >= 0 && rankIdx < lastRankIdx))
                            {
                                isNewSet = true;
                            }
                        }
                        else if (currentSet.Any(s => s.detectedRank == detectedRank))
                        {
                            isNewSet = true;
                        }
                    }

                    if (isNewSet && currentSet.Count > 0)
                    {
                        allSets.Add((currentVersion, currentSet));
                        currentSet = new List<(DanCourse course, string detectedRank, int rankIdx)>();
                        seenIndicesInCurrentSet.Clear();
                    }

                    logAction?.Invoke($"解析中: {detectedRank}");

                    var colMap = new Dictionary<int, string>();
                    int headerSongCol = -1;
                    
                    foreach (var kvp in absoluteCells)
                    {
                        var hc = kvp.Value;
                        int col = kvp.Key;
                        string txt = HtmlEntity.DeEntitize(hc.InnerText.Trim());
                        int cs = hc.GetAttributeValue("colspan", 1);
                        
                        if (headerSongCol == -1 && !string.IsNullOrEmpty(detectedRank) && txt.Contains(detectedRank) && cs >= 3)
                        {
                            headerSongCol = col;
                        }

                        // 「課題曲」列の特定をより厳密に
                        if (headerSongCol == -1 && (txt == "課題曲" || txt == "楽曲名" || txt == "曲名" || txt.Contains("課題曲")))
                        {
                            headerSongCol = col;
                        }
                        
                        string? type = null;
                        if (txt.Contains("魂ゲージ")) type = "Gauge";
                        else if (txt.Contains("不可")) type = "Miss";
                        else if (txt.Contains("良")) type = "Great";
                        else if (txt.Contains("可")) type = "Good";
                        else if (txt.Contains("連打数")) type = "Roll";
                        else if (txt.Contains("たたけた数") || txt.Contains("叩けた数")) type = "HitCount";
                        else if (txt.Contains("コンボ") || txt.Contains("最大コンボ数")) type = "MaxCombo";
                        else if (txt.Contains("最低スコア") || txt.Contains("スコア")) type = "Score";

                        if (type != null)
                        {
                            for (int k = 0; k < cs; k++) colMap[col + k] = type;
                        }
                    }

                    // 課題曲列が見つからない場合のフォールバック（リンクがある列を探す）
                    if (headerSongCol == -1) {
                        foreach (var kvp in absoluteCells) {
                            if (kvp.Value.SelectSingleNode(".//a") != null) { headerSongCol = kvp.Key; break; }
                        }
                    }
                    if (headerSongCol == -1) headerSongCol = 0;

                    // 条件列の開始位置を特定し、そこからの相対位置でマップを作成
                    int firstCondCol = colMap.Keys.Any() ? colMap.Keys.Min() : -1;
                    var relativeColMap = new Dictionary<int, string>();
                    if (firstCondCol != -1)
                    {
                        foreach (var kvp in colMap)
                        {
                            relativeColMap[kvp.Key - firstCondCol] = kvp.Value;
                        }
                    }

                    int songsAdded = 0;
                    var songRowsInfo = new List<(Dictionary<int, HtmlNode> absCells, HtmlNode row)>();
                    
                    // 曲の行を収集 (activeRowSpans を継続して更新)
                    for (int sIdx = 1; sIdx <= 15; sIdx++) // より深く探索
                    {
                        if (i + sIdx >= rows.Count) break;
                        var sRow = rows[i + sIdx];
                        
                        // 内部ループでも tableActiveRowSpans を更新し続ける必要があるが、
                        // 実際に i を進めるわけではないので、一時的なコピーを使う
                        int[] tempActiveRowSpans = (int[])tableActiveRowSpans.Clone();
                        // 実際には i + 1 から i + sIdx までの rowspan を考慮する必要がある
                        for (int skip = 1; skip < sIdx; skip++) {
                            GetAbsoluteCells(rows[i + skip], tempActiveRowSpans);
                        }
                        var sAbsCells = GetAbsoluteCells(sRow, tempActiveRowSpans);

                        if (sAbsCells.Count == 0) continue;
                        if (sAbsCells.Values.Any(c => c.InnerText.Contains("魂ゲージ") || c.InnerText.Contains("合格条件"))) break;
                        if (!IsSongRow(sAbsCells)) continue;

                        // 最も曲名らしいリンクを持つセルを探す
                        HtmlNode? bestSongCell = null;
                        string bestSongTitle = "";
                        foreach (var kvp in sAbsCells)
                        {
                            var links = kvp.Value.SelectNodes(".//a");
                            if (links == null) continue;
                            foreach (var link in links)
                            {
                                string t = HtmlEntity.DeEntitize(link.InnerText.Trim());
                                if (string.IsNullOrEmpty(t) || excludeKeywords.Contains(t) || t.Length < 2) continue;
                                if (t.Length > bestSongTitle.Length)
                                {
                                    bestSongTitle = t;
                                    bestSongCell = kvp.Value;
                                }
                            }
                        }

                        if (bestSongCell == null) continue;

                        // フォルダ名に使えない文字を削除
                        string safeSongTitle = NormalizationUtils.SanitizeFileName(bestSongTitle);
                        
                        string genre = "ナムコオリジナル";
                        var colorCell = sAbsCells.Values.FirstOrDefault(c => c.GetAttributeValue("style", "").Contains("background-color:#"));
                        if (colorCell != null) genre = MapGenreColor(GetStyleValue(colorCell, "background-color"));

                        string diffText = sRow.InnerText;
                        var diffCell = sAbsCells.Values.FirstOrDefault(c => c.InnerText.Contains("★"));
                        if (diffCell != null) diffText = diffCell.InnerText;
                        
                        bool isUra = bestSongTitle.Contains("(裏)") || diffText.Contains("裏") || diffText.Contains("(裏)");
                        string pathTitle = safeSongTitle.Replace("(裏)", "").Replace("(裏譜面)", "").Trim();
                        int difficulty = isUra ? 4 : DetectDifficulty(diffText);

                        dan.danSongs.Add(new DanSong { path = $"{pathTitle}.tja", difficulty = difficulty, genre = genre, isHidden = false });
                        songRowsInfo.Add((sAbsCells, sRow));
                        songsAdded++;

                        if (songsAdded >= 3) break;
                    }

                    if (songRowsInfo.Count > 0)
                    {
                        ParseConditionsFromAbsCells(songRowsInfo, relativeColMap, dan, excludeKeywords);
                    }

                    if (dan.danSongs.Count > 0)
                    {
                        currentSet.Add((dan, detectedRank, rankIdx));
                        if (rankIdx >= 0) seenIndicesInCurrentSet.Add(rankIdx);
                        lastParsedRankName = detectedRank;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { logAction?.Invoke($"  警告 ({detectedRank}): {ex.Message}"); }
            }
        }

        if (currentSet.Count > 0) allSets.Add((currentVersion, currentSet));

        // 各セットごとに処理
        for (int setIdx = 0; setIdx < allSets.Count; setIdx++)
        {
            var (versionName, set) = allSets[setIdx];
            // 外伝の場合はソートせず、ページの上から順番（追加された順）に処理する
            var sortedSet = isGaiden ? set : set.OrderBy(d => d.rankIdx).ToList();
            
            string setBaseFolder = outputDir;
            string danDefTitle = isGaiden ? "外伝段位" : "段位道場";

            if (allSets.Count > 1)
            {
                string folderName = setIdx.ToString();
                if (!isGaiden && !string.IsNullOrEmpty(versionName))
                {
                    // 外伝以外（過去バージョン等）は2024, 2023 などの西暦を抽出
                    var match = Regex.Match(versionName, @"20\d{2}");
                    if (match.Success)
                    {
                        folderName = match.Value;
                        danDefTitle = $"{match.Value}段位";
                    }
                    else
                    {
                        folderName = NormalizationUtils.SanitizeFolderName(versionName);
                        danDefTitle = $"{folderName}段位";
                    }
                }
                setBaseFolder = Path.Combine(outputDir, folderName);
            }
            else
            {
                // セットが1つの場合でも、タイトルを抽出する
                if (isGaiden)
                {
                    danDefTitle = "外伝段位";
                }
                else if (!string.IsNullOrEmpty(versionName))
                {
                    var match = Regex.Match(versionName, @"20\d{2}");
                    danDefTitle = match.Success ? $"{match.Value}段位" : $"{versionName}段位";
                }
            }
            
            if (!Directory.Exists(setBaseFolder)) Directory.CreateDirectory(setBaseFolder);

            // dan.def の生成
            string danDefPath = Path.Combine(setBaseFolder, "dan.def");
            await File.WriteAllTextAsync(danDefPath, $"#TITLE:{danDefTitle}", ct);

            int foundOrder = 0;
            foreach (var item in sortedSet)
            {
                ct.ThrowIfCancellationRequested();
                var dan = item.course;
                var detectedRank = item.detectedRank;
                
                string prefix = foundOrder.ToString("D2");
                string safeRankName = NormalizationUtils.SanitizeFolderName(detectedRank);
                string rankFolder = Path.Combine(setBaseFolder, $"{prefix} {safeRankName}");
                if (!Directory.Exists(rankFolder)) Directory.CreateDirectory(rankFolder);

                // Plate画像のコピー処理
                string? chosenPlatePath = null;
                if (plateMap != null)
                {
                    if (plateMap.TryGetValue(detectedRank, out var p) && File.Exists(p)) chosenPlatePath = p;
                    else if (plateMap.TryGetValue("*", out var def) && File.Exists(def)) chosenPlatePath = def;
                }

                if (chosenPlatePath != null)
                {
                    File.Copy(chosenPlatePath, Path.Combine(rankFolder, "Plate.png"), true);
                    dan.danPlatePath = "Plate.png";
                }
                else if (!string.IsNullOrEmpty(songsFolder) && Directory.Exists(songsFolder))
                {
                    string danPlateSource = Path.Combine(songsFolder, "Dan_Plate.png");
                    if (File.Exists(danPlateSource))
                    {
                        File.Copy(danPlateSource, Path.Combine(rankFolder, "Plate.png"), true);
                        dan.danPlatePath = "Plate.png";
                    }
                }

                if (!string.IsNullOrEmpty(songsFolder) && Directory.Exists(songsFolder))
                {
                    ct.ThrowIfCancellationRequested();
                    // 指定されたSongsフォルダ自体も含めて探索
                    var allDirs = new List<string> { songsFolder };
                    allDirs.AddRange(Directory.GetDirectories(songsFolder, "*", SearchOption.AllDirectories));
                    
                    var songsToKeep = new List<DanSong>();
                    var indicesToRemove = new List<int>();

                    for (int sIdx = 0; sIdx < dan.danSongs.Count; sIdx++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var s = dan.danSongs[sIdx];
                        string songNameRaw = Path.GetFileNameWithoutExtension(s.path); 
                        string songNameForSearch = songNameRaw.Replace("(裏譜面)", "").Replace("(裏)", "").Trim();
                        string? foundDir = FindDirectoryFuzzy(allDirs, songNameForSearch);
                        if (foundDir != null)
                        {
                            foreach (var file in Directory.GetFiles(foundDir))
                            {
                                string ext = Path.GetExtension(file).ToLower();
                                if (ext == ".tja") File.Copy(file, Path.Combine(rankFolder, s.path), true);
                                else if (ext == ".ogg" || ext == ".mp3") File.Copy(file, Path.Combine(rankFolder, Path.GetFileName(file)), true);
                            }
                            songsToKeep.Add(s);
                        }
                        else 
                        { 
                            missingSongs.Add($"[{detectedRank}] {songNameRaw}");
                            indicesToRemove.Add(sIdx);
                        }
                    }

                    // 見つからなかった曲を danSongs から削除し、対応する conditions の threshold も削除
                    if (indicesToRemove.Count > 0)
                    {
                        int originalSongCount = dan.danSongs.Count;
                        dan.danSongs = songsToKeep;
                        
                        foreach (var cond in dan.conditions)
                        {
                            // 曲数と同じ数の閾値がある場合のみ、曲に対応する閾値を削除する
                            // (1つしかない場合は、コース全体での条件とみなして削除しない)
                            if (cond.threshold.Count == originalSongCount)
                            {
                                // 逆順で削除してインデックスのずれを防ぐ
                                for (int rIdx = indicesToRemove.Count - 1; rIdx >= 0; rIdx--)
                                {
                                    int songIdx = indicesToRemove[rIdx];
                                    if (songIdx < cond.threshold.Count)
                                    {
                                        cond.threshold.RemoveAt(songIdx);
                                    }
                                }
                            }
                        }
                    }
                }

                string json = JsonSerializer.Serialize(dan, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                await File.WriteAllTextAsync(Path.Combine(rankFolder, "Dan.json"), json, ct);
                totalProcessed++; 
                foundOrder++;
            }
        }

        logAction?.Invoke($"生成完了: {totalProcessed} 件の段位を処理しました。");
        if (missingSongs.Count > 0)
        {
            logAction?.Invoke(""); logAction?.Invoke("=========== 見つからなかった曲一覧 ===========");
            foreach (var ms in missingSongs.Distinct()) logAction?.Invoke(ms);
            logAction?.Invoke("==============================================");
        }
    }

    private static string FindRankNameFromRow(HtmlAgilityPack.HtmlNode row, string[] rankNames, string[] excludeKeywords, bool isGaiden = false)
    {
        var potentialNodes = row.SelectNodes(".//strong | .//b | .//span[contains(@style, 'font-size')] | .//font");
        if (potentialNodes == null) return "";

        string bestMatch = "";
        foreach (var node in potentialNodes)
        {
            string txt = HtmlEntity.DeEntitize(node.InnerText.Trim());
            if (string.IsNullOrEmpty(txt) || txt.Length < 1) continue;
            if (excludeKeywords.Any(k => txt == k)) continue; 
            if (excludeKeywords.Any(k => txt.Contains(k)) && !rankNames.Any(rn => txt.Contains(rn))) continue; 
            if (IsInvalidRankName(txt, isGaiden)) continue;

            // 段位名らしいキーワードを含んでいるかチェック
            // 外伝の場合は、キーワードがなくても「段位名らしいノード（太字など）」であれば受理する
            bool isRankLike = isGaiden || 
                              rankNames.Any(rn => txt.Contains(rn)) || 
                              txt.EndsWith("級") || txt.EndsWith("段") || 
                              txt == "玄人" || txt == "名人" || txt == "超人" || txt == "達人";
            
            if (!isRankLike) continue;

            bestMatch = txt;
            if (rankNames.Any(rn => txt.Contains(rn))) return txt; 
        }
        return bestMatch;
    }

    private static Dictionary<int, HtmlNode> GetAbsoluteCells(HtmlNode row, int[] activeRowSpans)
    {
        var cells = row.SelectNodes(".//td");
        var absoluteCells = new Dictionary<int, HtmlNode>();
        if (cells == null)
        {
            for (int col = 0; col < activeRowSpans.Length; col++)
                if (activeRowSpans[col] > 0) activeRowSpans[col]--;
            return absoluteCells;
        }

        int cellIdx = 0;
        for (int col = 0; col < activeRowSpans.Length; col++)
        {
            if (activeRowSpans[col] > 0)
            {
                activeRowSpans[col]--;
                continue;
            }
            if (cellIdx >= cells.Count) break;
            var cell = cells[cellIdx];
            int cs = cell.GetAttributeValue("colspan", 1);
            int rs = cell.GetAttributeValue("rowspan", 1);
            absoluteCells[col] = cell;
            for (int k = 0; k < cs; k++)
                if (col + k < activeRowSpans.Length) activeRowSpans[col + k] = rs - 1;
            cellIdx++;
            col += cs - 1;
        }
        return absoluteCells;
    }

    private static bool IsSongRow(Dictionary<int, HtmlNode> absoluteCells)
    {
        // "1st", "2nd", "3rd" が含まれているか、
        // あるいは曲名らしいリンク（長いテキスト）と難易度（★）が含まれているか
        bool hasOrder = absoluteCells.Values.Any(c =>
        {
            string text = HtmlEntity.DeEntitize(c.InnerText.Trim());
            return text is "1st" or "2nd" or "3rd";
        });
        if (hasOrder) return true;

        bool hasSongLink = absoluteCells.Values.Any(c => {
            var a = c.SelectSingleNode(".//a");
            return a != null && HtmlEntity.DeEntitize(a.InnerText.Trim()).Length >= 2;
        });
        bool hasDiff = absoluteCells.Values.Any(c => c.InnerText.Contains("★"));

        return hasSongLink && hasDiff;
    }

    private static bool IsInvalidRankName(string txt, bool isGaiden = false)
    {
        if (string.IsNullOrWhiteSpace(txt)) return true;
        // 日付パターン
        if (Regex.IsMatch(txt, @"^\d+[\/\.]\d+") || Regex.IsMatch(txt, @"^\d+[\/\.]\d+[\/\.]\d+") || Regex.IsMatch(txt, @"^[\d\/\.～\-]+$")) return true;
        // 閾値パターン (87%以上, 100以上, 10未満, 5以下 など)
        if (Regex.IsMatch(txt, @"^\d+(%|％)?(以上|以下|未満)$") || Regex.IsMatch(txt, @"^\d+(%|％)$")) return true;
        if (txt.Contains("以上") || txt.Contains("以下") || txt.Contains("未満")) return true;
        // その他数値のみ
        if (Regex.IsMatch(txt, @"^\d+$")) return true;
        // あまりに長い文字列は段位名ではない (外伝は少し長めを許容)
        if (txt.Length > (isGaiden ? 50 : 20)) return true;
        return false;
    }

    private static string? FindDirectoryFuzzy(IEnumerable<string> dirs, string targetName)
    {
        string normalizedTarget = NormalizationUtils.NormalizeTitle(targetName);
        if (string.IsNullOrEmpty(normalizedTarget)) return null;
        
        var targetVariants = NormalizationUtils.ExpandTitleMatchKeys(normalizedTarget).ToList();
        
        foreach (var dir in dirs)
        {
            string dirName = Path.GetFileName(dir);
            string normalizedDirName = NormalizationUtils.NormalizeTitle(dirName);
            var dirVariants = NormalizationUtils.ExpandTitleMatchKeys(normalizedDirName).ToList();
            
            // 1. 完全一致（ターゲットとディレクトリのバリアントでチェック）
            if (targetVariants.Any(tv => dirVariants.Any(dv => dv.Equals(tv, StringComparison.OrdinalIgnoreCase))))
                return dir;
                
            // 2. 後方一致
            if (targetVariants.Any(tv => dirVariants.Any(dv => dv.EndsWith(tv, StringComparison.OrdinalIgnoreCase))))
                return dir;
                
            // 3. 部分一致
            if (targetVariants.Any(tv => dirVariants.Any(dv => dv.Contains(tv, StringComparison.OrdinalIgnoreCase))))
                return dir;
        }

        return null;
    }

    private static void ParseConditionsFromAbsCells(List<(Dictionary<int, HtmlNode> absCells, HtmlNode row)> songRowsInfo, Dictionary<int, string> relativeColMap, DanCourse dan, string[] excludeKeywords)
    {
        foreach (var item in songRowsInfo)
        {
            var absoluteCells = item.absCells;
            var row = item.row;

            // この行の曲タイトルの列を探す (リンクテキストが最も長いものを曲名とみなす)
            int currentSongCol = -1;
            int bestTitleLen = -1;
            
            foreach (var kvp in absoluteCells)
            {
                var cell = kvp.Value;
                int col = kvp.Key;
                var links = cell.SelectNodes(".//a");
                if (links != null)
                {
                    foreach (var link in links)
                    {
                        string t = HtmlEntity.DeEntitize(link.InnerText.Trim());
                        if (!string.IsNullOrEmpty(t) && !excludeKeywords.Contains(t) && t.Length > bestTitleLen)
                        {
                            bestTitleLen = t.Length;
                            currentSongCol = col;
                        }
                    }
                }
            }

            if (currentSongCol == -1) continue;

            // 曲名列の次にある「★（難易度）」や「コンボ」列を基準に、条件列の開始を特定する
            int songFirstCondCol = -1;
            int comboCol = -1;
            int diffCol = -1;

            foreach (var kvp in absoluteCells)
            {
                if (kvp.Key <= currentSongCol) continue;
                string txt = kvp.Value.InnerText;
                if (txt.Contains("コンボ")) comboCol = kvp.Key;
                if (txt.Contains("★")) diffCol = kvp.Key;
            }

            if (comboCol != -1) songFirstCondCol = comboCol + 1;
            else if (diffCol != -1) songFirstCondCol = diffCol + 1;
            else songFirstCondCol = currentSongCol + 3; // Fallback

            foreach (var kvp in absoluteCells)
            {
                int col = kvp.Key;
                var cell = kvp.Value;
                int relativeIdx = col - songFirstCondCol;

                if (relativeColMap.TryGetValue(relativeIdx, out string? type))
                {
                    // ヘッダーテキスト自体をパースしないように、数字が含まれているかチェック
                    if (!Regex.IsMatch(cell.InnerText, @"\d") && !cell.InnerText.Contains("?")) continue;

                    var (redV, goldV) = ExtractRedGold(cell, type);

                    if (redV.HasValue || goldV.HasValue)
                    {
                        int red = redV ?? 0;
                        int gold = goldV ?? 0;

                        if (type == "Gauge")
                        {
                            dan.conditionGauge.red = red;
                            dan.conditionGauge.gold = gold;
                        }
                        else
                        {
                            var cond = dan.conditions.FirstOrDefault(c => c.type == type);
                            if (cond == null)
                            {
                                cond = new Condition { type = type };
                                dan.conditions.Add(cond);
                            }
                            cond.threshold.Add(new Threshold { red = red, gold = gold });
                        }
                    }
                }
            }
        }
    }

    private static (int? red, int? gold) ExtractRedGold(HtmlNode cell, string type)
    {
        int? red = null;
        int? gold = null;
        bool redIsUncertain = false;
        bool goldIsUncertain = false;

        // Try to find spans with specific colors
        var redNode = cell.SelectSingleNode(".//span[contains(@style, '#f23b08') or contains(@style, 'color:red') or contains(@style, 'color:#ff0000')]");
        var goldNode = cell.SelectSingleNode(".//span[contains(@style, '#e8d03e') or contains(@style, 'color:gold') or contains(@style, 'color:#ffff00')]")
                    ?? cell.SelectSingleNode(".//strong");

        if (redNode != null) (red, redIsUncertain) = ExtractNumber(redNode.InnerText);
        if (goldNode != null) (gold, goldIsUncertain) = ExtractNumber(goldNode.InnerText);

        // Fallback: if only one value found or no spans found
        if (red == null || gold == null)
        {
            // "99%以上(金は100以上)" などのケースに対応するため、テキストを分割して抽出
            string text = cell.InnerText;
            var matches = Regex.Matches(text.Replace(",", ""), @"\d+");
            
            if (matches.Count >= 2)
            {
                if (red == null) red = int.Parse(matches[0].Value);
                if (gold == null) gold = int.Parse(matches[1].Value);
            }
            else if (matches.Count == 1)
            {
                int val = int.Parse(matches[0].Value);
                if (red == null) red = val;
                else if (gold == null) gold = val;
            }

            // 数字が見つからなかったが '?' が含まれる場合は 1 にする
            if (red == null && text.Contains("?")) { red = 1; redIsUncertain = true; }
            if (gold == null && text.Contains("?")) { gold = 1; goldIsUncertain = true; }
        }
        
        // Miss(不可) や Good(可) の場合は、小さい数値が gold であるべき (〇〇未満という条件のため)
        if (type == "Miss" || type == "MissCount" || type == "Good")
        {
            if (red.HasValue && gold.HasValue && red < gold)
            {
                // 片方が '?' 由来の場合は、入れ替えることで gold が red より小さくなる（難易度が上がる）なら入れ替える
                // ただし、既に正しい順序なら入れ替えない
                int? temp = red;
                red = gold;
                gold = temp;
            }
        }
        else
        {
            // それ以外（良、ゲージなど）は大きい数値が gold
            // ただし、gold が '?' 由来で 1 になっている場合は入れ替えを抑制する
            if (red.HasValue && gold.HasValue && gold < red)
            {
                if (!goldIsUncertain)
                {
                    int? temp = red;
                    red = gold;
                    gold = temp;
                }
            }
        }
        
        return (red, gold);
    }

    private static (int? value, bool isUncertain) ExtractNumber(string text)
    {
        if (string.IsNullOrEmpty(text)) return (null, false);
        var match = Regex.Match(text.Replace(",", ""), @"\d+");
        if (match.Success) return (int.Parse(match.Value), false);

        // 数字が見つからず、'?' が含まれる場合は 1 を返す (wikiで未判明の場合への対応)
        if (text.Contains("?")) return (1, true);

        return (null, false);
    }

    private static int DetectDifficulty(string text)
    {
        if (text.Contains("裏") || text.Contains("(裏)")) return 4;
        if (text.Contains("おに")) return 3;
        if (text.Contains("むずかしい")) return 2;
        if (text.Contains("ふつう")) return 1;
        if (text.Contains("かんたん")) return 0;
        return 3; 
    }

    private static string MapGenreColor(string color)
    {
        color = color.ToLower();
        if (color.Contains("#ff7028")) return "ナムコオリジナル";
        if (color.Contains("#4aaaba")) return "アニメ";
        if (color.Contains("#9966cc")) return "ボーカロイド™曲";
        if (color.Contains("#0099ff")) return "ゲームミュージック";
        if (color.Contains("#ffbb00") || color.Contains("#ded523")) return "バラエティ";
        if (color.Contains("#bda600")) return "クラシック";
        if (color.Contains("#ff4400")) return "ポップス";
        if (color.Contains("#ff66ff")) return "キッズ";
        return "ナムコオリジナル";
    }

    private static string GetStyleValue(HtmlAgilityPack.HtmlNode node, string property)
    {
        string style = node.GetAttributeValue("style", "");
        var match = Regex.Match(style, property + @":\s*([^;]+)");
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }
}

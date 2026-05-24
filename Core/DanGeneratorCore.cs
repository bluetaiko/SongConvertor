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
        var excludeKeywords = new[] { "合格条件", "お題", "お品書き", "魂ゲージ", "たたけた数", "叩けた数", "総音符数", "ノーツ数", "不可", "連打数", "良", "可", "コンボ", "最大コンボ数", "スコア", "動画", "計", "楽曲名", "課題曲", "難易度", "難しさ", "むずかしさ", "強さ", "★", "レベル", "概要", "詳細", "備考", "リンク", "プレイ動画", "参照", "初出", "回数", "解放期間", "解放条件" };

        var detectedRanks = new List<string>();
        string currentVersion = "";
        string currentSection = "";

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

                string rank = FindRankNameFromRow(row, rankNames, excludeKeywords);
                if (string.IsNullOrEmpty(rank) && i > 0) rank = FindRankNameFromRow(rows[i - 1], rankNames, excludeKeywords);
                
                if (string.IsNullOrEmpty(rank) && cellTexts.Count > 0)
                {
                    foreach (var cellText in cellTexts) {
                        if (string.IsNullOrEmpty(cellText) || cellText.Length < 2) continue;
                        bool isRankLike = rankNames.Any(rn => cellText.Contains(rn)) || 
                                          cellText.Contains("級") || cellText.Contains("段") || 
                                          cellText.Contains("名人") || cellText.Contains("達人") || cellText.Contains("玄人");
                        if (!isRankLike) continue;
                        if (excludeKeywords.Any(k => cellText.Contains(k))) continue;
                        if (IsInvalidRankName(cellText)) continue;
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

    public static async Task GenerateAsync(string inputSource, string outputDir, string songsFolder = "", string filter = "", Action<string>? logAction = null, Dictionary<string, string>? plateMap = null, CancellationToken ct = default)
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
        
        var excludeKeywords = new[] { "合格条件", "お題", "お品書き", "魂ゲージ", "たたけた数", "叩けた数", "総音符数", "ノーツ数", "不可", "連打数", "良", "可", "コンボ", "最大コンボ数", "スコア", "動画", "計", "楽曲名", "課題曲", "難易度", "難しさ", "むずかしさ", "強さ", "★", "レベル", "概要", "詳細", "備考", "リンク", "プレイ動画", "参照", "初出", "回数", "解放期間", "解放条件" };

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

            for (int i = 0; i < rows.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var row = rows[i];
                var cellNodes = row.SelectNodes(".//td");
                if (cellNodes == null) continue;

                var cellTexts = cellNodes.Select(c => HtmlEntity.DeEntitize(c.InnerText.Trim())).ToList();

                if (!cellTexts.Any(t => t.Contains("魂ゲージ") || t.Contains("合格条件") || t.Contains("可") || t.Contains("不可") || t.Contains("叩けた数"))) continue;

                string detectedRank = FindRankNameFromRow(row, rankNames, excludeKeywords);

                if (string.IsNullOrEmpty(detectedRank) && i > 0)
                {
                    var aboveRow = rows[i - 1];
                    detectedRank = FindRankNameFromRow(aboveRow, rankNames, excludeKeywords);
                }

                if (string.IsNullOrEmpty(detectedRank) && cellTexts.Count > 0)
                {
                    foreach (var cellText in cellTexts) {
                        if (string.IsNullOrEmpty(cellText) || cellText.Length < 2) continue;
                        // 段位名らしいキーワードを含んでいるかチェック
                        bool isRankLike = rankNames.Any(rn => cellText.Contains(rn)) || 
                                          cellText.Contains("級") || cellText.Contains("段") || 
                                          cellText.Contains("名人") || cellText.Contains("達人") || cellText.Contains("玄人");
                        
                        if (!isRankLike) continue;
                        if (excludeKeywords.Any(k => cellText.Contains(k))) continue;
                        if (IsInvalidRankName(cellText)) continue;
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
                    int rankIdx = isGaiden ? 19 : rankNames.ToList().FindIndex(r => detectedRank.Contains(r));
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

                    var headerCells = row.SelectNodes(".//td");
                    var colMap = new Dictionary<int, string>();
                    if (headerCells != null)
                    {
                        int colOffset = 0;
                        foreach (var hc in headerCells)
                        {
                            string txt = HtmlEntity.DeEntitize(hc.InnerText.Trim());
                            int cs = hc.GetAttributeValue("colspan", 1);
                            if (txt.Contains("魂ゲージ")) colMap[colOffset] = "Gauge";
                            else if (txt.Contains("不可")) colMap[colOffset] = "Miss";
                            else if (txt.Contains("良")) colMap[colOffset] = "Great";
                            else if (txt.Contains("可")) colMap[colOffset] = "Good";
                            else if (txt.Contains("連打数")) colMap[colOffset] = "Roll";
                            else if (txt.Contains("たたけた数") || txt.Contains("叩けた数")) colMap[colOffset] = "HitCount";
                            else if (txt.Contains("コンボ") || txt.Contains("最大コンボ数")) colMap[colOffset] = "MaxCombo";
                            else if (txt.Contains("最低スコア") || txt.Contains("スコア")) colMap[colOffset] = "Score";
                            colOffset += cs;
                        }
                    }

                    int songsAdded = 0;
                    var songRows = new List<HtmlNode>();
                    for (int sIdx = 1; sIdx <= 6; sIdx++) 
                    {
                        if (i + sIdx >= rows.Count) break;
                        var sRow = rows[i + sIdx];
                        var sCells = sRow.SelectNodes(".//td");
                        if (sCells == null || sCells.Count < 2) continue;
                        if (sCells.Any(c => c.InnerText.Contains("魂ゲージ") || c.InnerText.Contains("合格条件"))) break;

                        var a = sRow.SelectSingleNode(".//a");
                        if (a == null) continue;
                        string songTitle = HtmlEntity.DeEntitize(a.InnerText.Trim());
                        if (excludeKeywords.Contains(songTitle)) continue;

                        string rowText = sRow.InnerText;
                        if (!rowText.Contains("st") && !rowText.Contains("nd") && !rowText.Contains("rd") && songsAdded >= 3) break;

                        // フォルダ名に使えない文字を削除
                        string safeSongTitle = NormalizationUtils.SanitizeFileName(songTitle);
                        
                        string genre = "ナムコオリジナル";
                        var colorCell = sRow.SelectSingleNode(".//td[contains(@style, 'background-color:#')]");
                        if (colorCell != null) genre = MapGenreColor(GetStyleValue(colorCell, "background-color"));

                        string diffText = sRow.InnerText;
                        var diffCell = sCells.FirstOrDefault(c => c.InnerText.Contains("★"));
                        if (diffCell != null) diffText = diffCell.InnerText;
                        
                        bool isUra = songTitle.Contains("(裏)") || diffText.Contains("裏") || diffText.Contains("(裏)");
                        string pathTitle = safeSongTitle.Replace("(裏)", "").Replace("(裏譜面)", "").Trim();
                        int difficulty = isUra ? 4 : DetectDifficulty(diffText);

                        dan.danSongs.Add(new DanSong { path = $"{pathTitle}.tja", difficulty = difficulty, genre = genre });
                        songRows.Add(sRow);
                        songsAdded++;

                        if (songsAdded >= 3) break;
                    }

                    if (songRows.Count > 0)
                    {
                        ParseConditions(songRows, colMap, dan);
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
                    var allDirs = Directory.GetDirectories(songsFolder, "*", SearchOption.AllDirectories);
                    foreach (var s in dan.danSongs)
                    {
                        ct.ThrowIfCancellationRequested();
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
                        }
                        else { missingSongs.Add($"[{detectedRank}] {songNameRaw}"); }
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

    private static string FindRankNameFromRow(HtmlAgilityPack.HtmlNode row, string[] rankNames, string[] excludeKeywords)
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
            if (IsInvalidRankName(txt)) continue;

            bestMatch = txt;
            if (rankNames.Any(rn => txt.Contains(rn))) return txt; 
        }
        return bestMatch;
    }

    private static bool IsInvalidRankName(string txt)
    {
        if (string.IsNullOrWhiteSpace(txt)) return true;
        // 日付パターン
        if (Regex.IsMatch(txt, @"^\d+[\/\.]\d+") || Regex.IsMatch(txt, @"^\d+[\/\.]\d+[\/\.]\d+") || Regex.IsMatch(txt, @"^[\d\/\.～\-]+$")) return true;
        // 閾値パターン (87%以上, 100以上, 10未満, 5以下 など)
        if (Regex.IsMatch(txt, @"^\d+(%|％)?(以上|以下|未満)$") || Regex.IsMatch(txt, @"^\d+(%|％)$")) return true;
        if (txt.Contains("以上") || txt.Contains("以下") || txt.Contains("未満")) return true;
        // その他数値のみ
        if (Regex.IsMatch(txt, @"^\d+$")) return true;
        // あまりに長い文字列は段位名ではない
        if (txt.Length > 50) return true;
        return false;
    }

    private static string? FindDirectoryFuzzy(string[] dirs, string targetName)
    {
        string normalizedTarget = NormalizationUtils.NormalizeTitle(targetName);
        if (string.IsNullOrEmpty(normalizedTarget)) return null;
        var match = dirs.FirstOrDefault(d => NormalizationUtils.NormalizeTitle(Path.GetFileName(d)).EndsWith(normalizedTarget));
        if (match != null) return match;
        match = dirs.FirstOrDefault(d => NormalizationUtils.NormalizeTitle(Path.GetFileName(d)).Contains(normalizedTarget));
        if (match != null) return match;
        return null;
    }

    private static void ParseConditions(List<HtmlNode> rows, Dictionary<int, string> colMap, DanCourse dan)
    {
        int maxCols = 30; 
        int[] activeRowSpans = new int[maxCols];

        foreach (var row in rows)
        {
            var cells = row.SelectNodes(".//td");
            if (cells == null) continue;

            int cellIdx = 0;
            for (int col = 0; col < maxCols; col++)
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

                if (colMap.TryGetValue(col, out string? type))
                {
                    var redSpan = cell.SelectSingleNode(".//span[contains(@style, '#f23b08')]") 
                               ?? cell.SelectSingleNode(".//span[contains(@style, 'color:red')]")
                               ?? cell.SelectSingleNode(".//span[contains(@style, 'color:#ff0000')]");
                               
                    var goldSpan = cell.SelectSingleNode(".//span[contains(@style, '#e8d03e')]") 
                                ?? cell.SelectSingleNode(".//strong")
                                ?? cell.SelectSingleNode(".//span[contains(@style, 'color:#ffff00')]")
                                ?? cell.SelectSingleNode(".//span[contains(@style, 'color:gold')]");
                    
                    if (redSpan != null && goldSpan != null)
                    {
                        int redV = ExtractNumber(redSpan.InnerText);
                        int goldV = ExtractNumber(goldSpan.InnerText);

                        if (type == "Gauge")
                        {
                            dan.conditionGauge.red = redV;
                            dan.conditionGauge.gold = goldV;
                        }
                        else
                        {
                            var cond = dan.conditions.FirstOrDefault(c => c.type == type);
                            if (cond == null)
                            {
                                cond = new Condition { type = type };
                                dan.conditions.Add(cond);
                            }
                            cond.threshold.Add(new Threshold { red = redV, gold = goldV });
                        }
                    }
                }

                for (int i = 0; i < cs; i++)
                {
                    if (col + i < maxCols)
                        activeRowSpans[col + i] = rs - 1;
                }

                cellIdx++;
                col += cs - 1;
            }
        }
    }

    private static int ExtractNumber(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var match = Regex.Match(text.Replace(",", ""), @"\d+");
        return match.Success ? int.Parse(match.Value) : 0;
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

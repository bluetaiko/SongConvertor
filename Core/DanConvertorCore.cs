using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SongConverter.Utils;

namespace SongConverter.Core;

public class DanConvertorCore
{
    private static readonly string[] MusicExtensions = { ".ogg", ".mp3", ".wav", ".wma", ".xa" };

    public static async Task ConvertAsync(string tjaPath, string outputRoot, string simuFolder, Action<string>? logAction = null, Dictionary<string, string>? assetMap = null, CancellationToken ct = default)
    {
        if (!File.Exists(tjaPath)) return;

        string tjaContent = await File.ReadAllTextAsync(tjaPath, Encoding.GetEncoding(932), ct);
        string[] lines = tjaContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        var globalMeta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            if (line.StartsWith("#NEXTSONG", StringComparison.OrdinalIgnoreCase)) break;
            if (line.StartsWith("#START", StringComparison.OrdinalIgnoreCase)) break;
            var match = Regex.Match(line, @"^([A-Z0-9]+):\s*(.*)$", RegexOptions.IgnoreCase);
            if (match.Success) globalMeta[match.Groups[1].Value] = match.Groups[2].Value.Trim();
        }

        string tjaFileName = Path.GetFileNameWithoutExtension(tjaPath);
        string safeTitle = string.Join("_", tjaFileName.Split(Path.GetInvalidFileNameChars()));
        string outputDir = Path.Combine(outputRoot, safeTitle);
        Directory.CreateDirectory(outputDir);

        string? courseTitle = globalMeta.GetValueOrDefault("TITLE") ?? tjaFileName;
        logAction?.Invoke($"分割優先変換を開始: {courseTitle} -> {outputDir}");

        var danJson = new DanJson { title = courseTitle, danIndex = 19 };

        // 外部から指定された画像アセットの処理
        if (assetMap != null)
        {
            var mapping = new Dictionary<string, (string key, string targetName)>
            {
                { "danPlatePath", ("danPlatePath", "Plate.png") },
                { "danPanelSidePath", ("danPanelSidePath", "panelside.png") },
                { "danTitlePlatePath", ("danTitlePlatePath", "titleplate.png") },
                { "danMiniPlatePath", ("danMiniPlatePath", "miniplate.png") }
            };

            foreach (var map in mapping)
            {
                if (assetMap.TryGetValue(map.Key, out var sourcePath) && File.Exists(sourcePath))
                {
                    File.Copy(sourcePath, Path.Combine(outputDir, map.Value.targetName), true);
                    if (map.Value.key == "danPlatePath") danJson.danPlatePath = map.Value.targetName;
                    else if (map.Value.key == "danPanelSidePath") danJson.danPanelSidePath = map.Value.targetName;
                    else if (map.Value.key == "danTitlePlatePath") danJson.danTitlePlatePath = map.Value.targetName;
                    else if (map.Value.key == "danMiniPlatePath") danJson.danMiniPlatePath = map.Value.targetName;
                }
            }
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("#NEXTSONG", StringComparison.OrdinalIgnoreCase)) break;
            var trimmed = line.Trim();
            if (trimmed.StartsWith("EXAM", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(trimmed, @"^EXAM\d*:\s*(.*)$", RegexOptions.IgnoreCase);
                if (match.Success) ParseExam(match.Groups[1].Value, danJson.conditions, danJson);
            }
        }

        var sections = SplitIntoSections(lines, globalMeta);
        var finalSongs = new List<DanSong>();
        
        string localDir = Path.GetDirectoryName(tjaPath)!;
        string[]? allSimuFiles = !string.IsNullOrEmpty(simuFolder) && Directory.Exists(simuFolder)
                               ? await Task.Run(() => Directory.GetFiles(simuFolder, "*.*", SearchOption.AllDirectories), ct)
                               : null;

        foreach (var section in sections)
        {
            ct.ThrowIfCancellationRequested();
            string targetTjaName = Path.ChangeExtension(section.Wave, ".tja");
            string tjaPathOut = Path.Combine(outputDir, targetTjaName);
            var sb = new StringBuilder();
            sb.AppendLine($"TITLE:{section.Title}");
            sb.AppendLine($"SUBTITLE:{section.Subtitle}");
            sb.AppendLine($"BPM:{section.BPM}");
            sb.AppendLine($"WAVE:{section.Wave}");
            sb.AppendLine($"OFFSET:{section.Offset}");
            sb.AppendLine($"GENRE:{section.Genre}");
            sb.AppendLine($"COURSE:{section.Course}");
            sb.AppendLine($"LEVEL:{globalMeta.GetValueOrDefault("LEVEL", "10")}");
            if (!string.IsNullOrEmpty(section.Balloon)) sb.AppendLine($"BALLOON:{section.Balloon}");
            else if (globalMeta.ContainsKey("BALLOON")) sb.AppendLine($"BALLOON:{globalMeta["BALLOON"]}");
            if (!string.IsNullOrEmpty(section.ScoreInit)) sb.AppendLine($"SCOREINIT:{section.ScoreInit}");
            if (!string.IsNullOrEmpty(section.ScoreDiff)) sb.AppendLine($"SCOREDIFF:{section.ScoreDiff}");
            if (globalMeta.ContainsKey("SCOREMODE")) sb.AppendLine($"SCOREMODE:{globalMeta["SCOREMODE"]}");
            sb.AppendLine("");
            sb.AppendLine("#START");
            sb.AppendLine("");

            // Contentには#NEXTSONG以降の譜面データが含まれている
            foreach (var l in section.Content)
            {
                if (l.Trim().StartsWith("COURSE:", StringComparison.OrdinalIgnoreCase)) continue;
                sb.AppendLine(l);
            }

            await File.WriteAllTextAsync(tjaPathOut, sb.ToString(), new UTF8Encoding(false), ct);
            
            // Find music file (Dan TJA folder first, then Simulator folder)
            string musicPath = Path.Combine(localDir, section.Wave);
            string? waveFallback = File.Exists(musicPath) ? musicPath
                                  : allSimuFiles?.FirstOrDefault(f => Path.GetFileName(f).Equals(section.Wave, StringComparison.OrdinalIgnoreCase));
            
            if (waveFallback != null)
            {
                File.Copy(waveFallback, Path.Combine(outputDir, section.Wave), true);
                logAction?.Invoke($"  分割譜面を生成 + 音源採取: {section.Title}");
            }
            else
            {
                logAction?.Invoke($"  警告: 音源が見つかりませんでした (要確認): {section.Title}");
            }

            string danPlateSource = Path.Combine(localDir, "Dan_Plate.png");
            if (File.Exists(danPlateSource))
            {
                // すでに外部指定で Plate.png がコピーされている場合はスキップ
                if (danJson.danPlatePath == null)
                {
                    File.Copy(danPlateSource, Path.Combine(outputDir, "Plate.png"), true);
                    danJson.danPlatePath = "Plate.png";
                }
            }

            finalSongs.Add(new DanSong { path = targetTjaName, genre = section.Genre, difficulty = 3 });
        }

        danJson.danSongs = finalSongs.ToArray();
        string jsonPath = Path.Combine(outputDir, "Dan.json");
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(danJson, options), new UTF8Encoding(false), ct);
        logAction?.Invoke($"完了: {jsonPath}");
    }

    private static List<SplitSection> SplitIntoSections(string[] lines, Dictionary<string, string> globalMeta)
    {
        var sections = new List<SplitSection>();
        SplitSection? current = null;
        string lastBpm = globalMeta.GetValueOrDefault("BPM", "120");
        string lastCourse = "4";
        bool inSongBlock = false;  // #NEXTSONG 検出後 〜 #END まで

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("#NEXTSONG", StringComparison.OrdinalIgnoreCase))
            {
                // 前の曲を確定
                if (current != null)
                {
                    // 末尾の空行を除去
                    while (current.Content.Count > 0 && string.IsNullOrWhiteSpace(current.Content[current.Content.Count - 1]))
                        current.Content.RemoveAt(current.Content.Count - 1);
                    sections.Add(current);
                }

                // 次の曲を開始
                var parts = trimmed.Substring(9).Split(',');
                current = new SplitSection
                {
                    Title = parts.Length > 0 ? parts[0].Trim() : "Untitled",
                    Subtitle = parts.Length > 1 ? parts[1].Trim() : "",
                    Genre = parts.Length > 2 ? parts[2].Trim() : "",
                    Wave = parts.Length > 3 ? parts[3].Trim() : "song.ogg",
                    ScoreInit = parts.Length > 4 ? parts[4].Trim() : "",
                    ScoreDiff = parts.Length > 5 ? parts[5].Trim() : "",
                    DemoStart = parts.Length > 6 ? parts[6].Trim() : "0",
                    // Offsetは#DELAYから計算するため、ここでは初期値のみ
                    BPM = lastBpm,
                    Course = "Oni"
                };
                inSongBlock = true;  // #NEXTSONG 以降は曲の内容
            }
            else if (trimmed.StartsWith("COURSE:", StringComparison.OrdinalIgnoreCase))
            {
                var courseVal = trimmed.Substring(7).Trim();
                if (current != null) current.Course = courseVal;
                if (!inSongBlock) lastCourse = courseVal;
                if (current != null && inSongBlock) current.Content.Add(line);
            }
            else if (trimmed.StartsWith("DEMOSTART:", StringComparison.OrdinalIgnoreCase))
            {
                var demoVal = trimmed.Substring(10).Trim();
                if (current != null) current.DemoStart = demoVal;
            }
            else if (trimmed.StartsWith("#BPMCHANGE", StringComparison.OrdinalIgnoreCase))
            {
                // BPM変更を記録
                lastBpm = trimmed.Substring(10).Trim();
                if (current != null)
                {
                    current.BPM = lastBpm;
                    if (inSongBlock) current.Content.Add(line);
                }
            }
            else if (trimmed.StartsWith("#DELAY", StringComparison.OrdinalIgnoreCase))
            {
                // #DELAYでOFFSETを計算（Contentには追加しない）
                if (current != null && inSongBlock)
                {
                    var delayValue = trimmed.Substring(6).Trim();
                    if (double.TryParse(delayValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double delay))
                    {
                        current.Offset = (-delay).ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                // #DELAYはContentには出力しない
            }
            else if (trimmed.StartsWith("#BALLOON", StringComparison.OrdinalIgnoreCase))
            {
                // #BALLOONを記録（Contentには追加しない）
                if (current != null && inSongBlock)
                {
                    var balloonValue = trimmed.Substring(8).Trim();
                    if (balloonValue.StartsWith(":")) balloonValue = balloonValue.Substring(1).Trim();
                    if (!string.IsNullOrEmpty(balloonValue)) current.Balloon = balloonValue;
                }
                // Contentには追加しない
            }
            else if (trimmed.StartsWith("#MEASURE", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("#SCROLL", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("#BARLINE", StringComparison.OrdinalIgnoreCase))
            {
                // これらのヘッダー行はContentに追加
                if (current != null && inSongBlock) current.Content.Add(line);
            }
            else if (current != null && inSongBlock)
            {
                // EXAM行はスキップ（Contentに追加しない）
                if (trimmed.StartsWith("EXAM", StringComparison.OrdinalIgnoreCase)) continue;
                
                // 譜面開始前の空行もスキップ（#DELAY/#BALLOON検出のため）
                if (string.IsNullOrWhiteSpace(trimmed) && current.Content.Count == 0) continue;
                
                // #NEXTSONG 以降の譜面データを追加
                current.Content.Add(line);
                if (trimmed.Equals("#END", StringComparison.OrdinalIgnoreCase))
                    inSongBlock = false;
            }
        }

        // 最後の曲を確定
        if (current != null)
        {
            while (current.Content.Count > 0 && string.IsNullOrWhiteSpace(current.Content[current.Content.Count - 1]))
                current.Content.RemoveAt(current.Content.Count - 1);
            sections.Add(current);
        }

        return sections;
    }

    private static void ParseExam(string content, List<Condition> targetList, DanJson? root)
    {
        var parts = content.Split(',');
        if (parts.Length < 3) return;
        string typeCode = parts[0].Trim().ToLower();
        if (!int.TryParse(parts[1], out int red) || !int.TryParse(parts[2], out int gold)) return;
        if (typeCode == "g" && root != null) root.conditionGauge = new ConditionGauge { red = red, gold = gold };
        else
        {
            string typeName = typeCode switch { "jp" => "Great", "jg" => "Good", "jb" => "Miss", "s" => "Score", "r" => "Roll", "h" => "Hit", "c" => "Combo", _ => "Other" };
            targetList.Add(new Condition { type = typeName, threshold = new List<Threshold> { new Threshold { red = red, gold = gold } } });
        }
    }

    private class SplitSection
    {
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Genre { get; set; } = "";
        public string Wave { get; set; } = "";
        public string ScoreInit { get; set; } = "";
        public string ScoreDiff { get; set; } = "";
        public string BPM { get; set; } = "";
        public string Offset { get; set; } = "0";
        public string DemoStart { get; set; } = "";
        public string Course { get; set; } = "4";
        public string Balloon { get; set; } = "";
        public List<string> Content { get; set; } = new();
        public List<Condition> Conditions { get; set; } = new();
    }

    private class DanJson
    {
        public string title { get; set; } = "";
        public int danIndex { get; set; } = 0;
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? danPlatePath { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? danPanelSidePath { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? danTitlePlatePath { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? danMiniPlatePath { get; set; }
        public DanSong[] danSongs { get; set; } = Array.Empty<DanSong>();
        public ConditionGauge? conditionGauge { get; set; }
        public List<Condition> conditions { get; set; } = new();
    }

    private class DanSong
    {
        public string path { get; set; } = "";
        public int difficulty { get; set; } = 3;
        public string genre { get; set; } = "";
        public bool isHidden { get; set; } = false;
        public List<Condition>? conditions { get; set; }
    }

    private class ConditionGauge { public int red { get; set; } public int gold { get; set; } }
    private class Condition { public string type { get; set; } = ""; public List<Threshold> threshold { get; set; } = new(); }
    private class Threshold { public int red { get; set; } public int gold { get; set; } }
}

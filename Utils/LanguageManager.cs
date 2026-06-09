using Microsoft.Win32;

namespace SongConverter.Utils;

public enum Language
{
    Japanese,
    English
}

public static class LanguageManager
{
    public static Language CurrentLanguage { get; private set; } = Language.Japanese;

    private static readonly Dictionary<string, string> Ja = new()
    {
        { "TabAddSongs", "曲追加" },
        { "TabSongSorter", "並び替え" },
        { "TabDanGenerator", "段位生成" },
        { "TabDanConvertor", "段位変換" },
        { "Language", "言語" },
        { "SelectDownloadFolder", "楽曲をダウンロードするフォルダを選択:" },
        { "Browse", "参照..." },
        { "ExecuteAddSongs", "曲追加実行" },
        { "SelectSourceSongs", "コピー元のSongsフォルダを選択:" },
        { "SelectDestSongs", "コピー先のSongsフォルダを選択:" },
        { "UpdateSongList", "曲名リスト更新" },
        { "StartSort", "並び替え開始" },
        { "WikiUrl", "太鼓Wikiの段位URL:" },
        { "FilterDan", "特定の段位のみ抽出:" },
        { "DanIndex", "DanIndex:" },
        { "OutputFolder", "出力フォルダ:" },
        { "SelectSongsFolder", "Songsフォルダを選択:" },
        { "GenerateDan", "段位生成" },
        { "TjaFile", "元となるTJAファイル:" },
        { "DanConvertOutputFolder", "出力フォルダ:" },
        { "MiniPlateText", "段位名:" },
        { "DanConvertSimu", "Songsフォルダ (省略可・フォールバック用):" },
        { "ExecuteConvert", "変換実行" },
        { "Ready", "準備完了。" },
        { "ConvertingTja", "TJA 変換中..." },
        { "StartTjaConvert", "TJA から段位への変換を開始します..." },
        { "WarnSelectTja", "変換対象のTJAを選択してください。" },
        { "WarnSelectOutput", "出力フォルダを選択してください。" },
        { "Warn", "警告" },
        { "Done", "完了" },
        { "Error", "エラー" },
        { "Processing", "処理中..." },
        { "Cancel", "キャンセル" },
        { "CategoryAll", "カテゴリー: 全て" },
        { "CategorySelect", "カテゴリー選択" },
        { "PlateSettings", "Plate画像選択" },
        { "WikiUrlFirst", "先にWiki URLを入力してください。" },
        { "DanNameNotFound", "段位名が見つかりませんでした。URLを確認してください。" },
        { "PlateIndividualSettings", "Plate画像個別設定" },
        { "ApplyToAllDan", "【すべての段位に適用】" },
        { "Save", "保存" },
        { "SelectImage", "画像選択" },
        { "ImageSettingsConv", "画像設定 (DanConvertor)" },
        { "Interrupt", "中断" },
        { "Interrupted", "中断を受け付けました。" },
        { "All", "全て" },
        { "None", "なし" },
        { "OK", "決定" },
        { "FetchingList", "譜面リスト取得中..." },
        { "StartFetchList", "公開譜面リストの取得を開始します。" },
        { "FetchDone", "譜面リストの更新が完了しました。" },
        { "UserCancelled", "ユーザー操作で中断しました。" },
        { "Organizing", "曲フォルダー整理中..." },
        { "StartOrganize", "曲フォルダー整理を開始します。" },
        { "DanGenerating", "段位生成中..." },
        { "StartDanGenerate", "段位生成を開始します。" },
        { "DanGenerateDone", "段位生成が完了しました。" },
        { "NoValidTja", "有効なTJAファイルが見つかりませんでした。" },
        { "ProcessingCount", "{0} 個のファイルを処理します。" },
        { "ProcessingFile", "処理開始: {0}" },
        { "ConvertDoneCount", "{0} 件の変換が完了しました。" },
        { "ConvertDone", "すべての変換が完了しました。" },
        { "AddSongsSyncing", "AddSongs 同期中..." },
        { "StartAddSongsSync", "AddSongs 同期を開始します。" },
        { "GitNotFound", "Git がインストールされていません。" },
        { "GitNeeded", "Git が必要です。https://git-scm.com/ からインストールしてください。" },
        { "ExistingRepoFound", "既存の Songs リポジトリを検出しました。pull を実行します。" },
        { "CloneRepo", "Songs リポジトリが見つからないため clone を実行します。" },
        { "AddSongsSyncDone", "AddSongs 同期が完了しました。" },
        { "Wait", "待機中" },
        { "CategoryCount", "カテゴリー: {0}/{1}" },
        { "WarnSelectSourceSimu", "コピー元とシミュフォルダを設定してください。" },
        { "WarnInputWikiSimu", "Wiki URLとシミュフォルダを入力してください。" },
        { "WarnSelectWorkFolder", "作業フォルダーを設定してください。" },
        { "CatPops", "00 ポップス" },
        { "CatKids", "01 キッズ" },
        { "CatAnime", "02 アニメ" },
        { "CatVocaloid", "03 ボーカロイド™曲" },
        { "CatGame", "04 ゲームミュージック" },
        { "CatVariety", "05 バラエティ" },
        { "CatClassic", "06 クラシック" },
        { "CatNamco", "07 ナムコオリジナル" }
    };

    private static readonly Dictionary<string, string> En = new()
    {
        { "TabAddSongs", "Add Songs" },
        { "TabSongSorter", "Sorter" },
        { "TabDanGenerator", "Dan Gen" },
        { "TabDanConvertor", "Dan Conv" },
        { "Language", "Language" },
        { "SelectDownloadFolder", "Select folder to download songs:" },
        { "Browse", "Browse..." },
        { "ExecuteAddSongs", "Add Songs" },
        { "SelectSourceSongs", "Select source Songs folder:" },
        { "SelectDestSongs", "Select destination Songs folder:" },
        { "UpdateSongList", "Update List" },
        { "StartSort", "Start Sorting" },
        { "WikiUrl", "Taiko Wiki Dan URL:" },
        { "FilterDan", "Filter specific Dan:" },
        { "DanIndex", "DanIndex:" },
        { "OutputFolder", "Output Folder:" },
        { "SelectSongsFolder", "Select Songs folder:" },
        { "GenerateDan", "Generate Dan" },
        { "TjaFile", "Source TJA File:" },
        { "DanConvertOutputFolder", "Output Folder:" },
        { "MiniPlateText", "Dan Name:" },
        { "DanConvertSimu", "Songs Folder (Optional):" },
        { "ExecuteConvert", "Convert" },
        { "Ready", "Ready." },
        { "ConvertingTja", "Converting TJA..." },
        { "StartTjaConvert", "Starting TJA to Dan conversion..." },
        { "WarnSelectTja", "Please select a TJA file." },
        { "WarnSelectOutput", "Please select an output folder." },
        { "Warn", "Warning" },
        { "Done", "Done" },
        { "Error", "Error" },
        { "Processing", "Processing..." },
        { "Cancel", "Cancel" },
        { "CategoryAll", "Category: All" },
        { "CategorySelect", "Select Categories" },
        { "PlateSettings", "Plate Image Settings" },
        { "WikiUrlFirst", "Please enter Wiki URL first." },
        { "DanNameNotFound", "Dan name not found. Please check URL." },
        { "PlateIndividualSettings", "Individual Plate Settings" },
        { "ApplyToAllDan", "【Apply to all Dan】" },
        { "Save", "Save" },
        { "SelectImage", "Select Image" },
        { "ImageSettingsConv", "Image Settings (DanConvertor)" },
        { "Interrupt", "Stop" },
        { "Interrupted", "Interrupted." },
        { "All", "All" },
        { "None", "None" },
        { "OK", "OK" },
        { "FetchingList", "Fetching song list..." },
        { "StartFetchList", "Starting to fetch song list." },
        { "FetchDone", "Song list updated successfully." },
        { "UserCancelled", "Cancelled by user." },
        { "Organizing", "Organizing folders..." },
        { "StartOrganize", "Starting to organize folders." },
        { "DanGenerating", "Generating Dan..." },
        { "StartDanGenerate", "Starting Dan generation." },
        { "DanGenerateDone", "Dan generation completed." },
        { "NoValidTja", "No valid TJA files found." },
        { "ProcessingCount", "Processing {0} files." },
        { "ProcessingFile", "Processing: {0}" },
        { "ConvertDoneCount", "{0} conversions completed." },
        { "ConvertDone", "Conversion completed." },
        { "AddSongsSyncing", "Syncing AddSongs..." },
        { "StartAddSongsSync", "Starting AddSongs sync." },
        { "GitNotFound", "Git is not installed." },
        { "GitNeeded", "Git is required. Please install from https://git-scm.com/" },
        { "ExistingRepoFound", "Existing Songs repo found. Running pull." },
        { "CloneRepo", "Songs repo not found. Running clone." },
        { "AddSongsSyncDone", "AddSongs sync completed." },
        { "Wait", "Idle" },
        { "CategoryCount", "Category: {0}/{1}" },
        { "WarnSelectSourceSimu", "Please set source and Songs folders." },
        { "WarnInputWikiSimu", "Please enter Wiki URL and Songs folder." },
        { "WarnSelectWorkFolder", "Please set work folder." },
        { "CatPops", "00 Pops" },
        { "CatKids", "01 Kids" },
        { "CatAnime", "02 Anime" },
        { "CatVocaloid", "03 VOCALOID™" },
        { "CatGame", "04 Game Music" },
        { "CatVariety", "05 Variety" },
        { "CatClassic", "06 Classical" },
        { "CatNamco", "07 NAMCO Original" }
    };

    static LanguageManager()
    {
        InitializeLanguage();
    }

    public static void InitializeLanguage()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\SongConverter");
            if (key?.GetValue("Language") is string lang)
            {
                if (lang.Equals("english", StringComparison.OrdinalIgnoreCase))
                {
                    CurrentLanguage = Language.English;
                    return;
                }
            }
        }
        catch { }

        // Fallback to system language
        if (System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "ja")
        {
            CurrentLanguage = Language.English;
        }
    }

    public static void SetLanguage(Language lang)
    {
        CurrentLanguage = lang;
    }

    public static string GetString(string key)
    {
        var dict = CurrentLanguage == Language.English ? En : Ja;
        return dict.TryGetValue(key, out var val) ? val : key;
    }
}

namespace SongConverter.Models;

public class DanCourse
{
    public string title { get; set; } = string.Empty;
    public int danIndex { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? danPlatePath { get; set; }
    public List<DanSong> danSongs { get; set; } = new();
    public ConditionGauge conditionGauge { get; set; } = new();
    public List<Condition> conditions { get; set; } = new();
}

public class DanSong
{
    public string path { get; set; } = string.Empty;
    public int difficulty { get; set; }
    public string genre { get; set; } = string.Empty;
    public bool isHidden { get; set; } = false;
}

public class ConditionGauge
{
    public int red { get; set; }
    public int gold { get; set; }
}

public class Condition
{
    public string type { get; set; } = string.Empty;
    public List<Threshold> threshold { get; set; } = new();
}

public class Threshold
{
    public int red { get; set; }
    public int gold { get; set; }
}
